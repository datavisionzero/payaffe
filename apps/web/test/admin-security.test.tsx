import { fireEvent, screen } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminAccountPage } from "../components/admin/account-page";
import { AdminAuditLogDetailPage } from "../components/admin/audit-log-detail-page";
import { AdminAuditLogPage } from "../components/admin/audit-log-page";
import {
  adminServer,
  auditEventId,
  renderAdmin,
  resetAdminState,
  session,
  state
} from "./admin-harness";

const nav = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn() }));
vi.mock("../lib/navigation", () => ({
  useRouter: () => ({
    back: vi.fn(),
    forward: vi.fn(),
    prefetch: vi.fn(),
    push: nav.push,
    refresh: vi.fn(),
    replace: nav.replace
  }),
  usePathname: () => "/admin/audit-log",
  useSearchParams: () => new URLSearchParams()
}));

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminAuditLogPage", () => {
  it("lists entries and links each one to its own route", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAuditLogPage />);

    expect(await screen.findByRole("link", { name: "admin.audit_log.list" })).toHaveAttribute(
      "href",
      `/admin/audit-log/${auditEventId}`
    );
    expect(screen.getByText("audit_log.listed")).toBeInTheDocument();
  });

  it("exports with CSRF and hands the browser a file", async () => {
    state.authenticated = true;
    const downloadClick = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined);
    renderAdmin(<AdminAuditLogPage />);

    await screen.findByRole("link", { name: "admin.audit_log.list" });
    fireEvent.click(screen.getByRole("button", { name: "Export JSON" }));

    await vi.waitFor(() => expect(state.csrf.auditExport).toBe("csrf-token"));
    expect(downloadClick).toHaveBeenCalledOnce();
    expect(await screen.findByText(/Exported 1 event/)).toBeInTheDocument();
  });
});

describe("AdminAuditLogDetailPage", () => {
  it("shows the source and correlation identifier of one entry", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAuditLogDetailPage eventId={auditEventId} />);

    expect(
      await screen.findByRole("heading", { level: 1, name: "admin.audit_log.list" })
    ).toBeInTheDocument();
    expect(await screen.findByText("203.0.113.10")).toBeInTheDocument();
    expect(screen.getByText("SensitiveUserAgent/1.0")).toBeInTheDocument();
    expect(screen.getByText("trace-123")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to audit log" })).toHaveAttribute(
      "href",
      "/admin/audit-log"
    );
  });

  it("reports a missing entry without leaking why", async () => {
    state.authenticated = true;
    adminServer.use(
      http.get(`/api/admin/audit-log/${auditEventId}`, () =>
        HttpResponse.json(
          { title: "Not found.", code: "audit_log.not_found" },
          { status: 404 }
        )
      )
    );
    renderAdmin(<AdminAuditLogDetailPage eventId={auditEventId} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The Audit Log entry was not found."
    );
  });
});

describe("AdminAccountPage", () => {
  it("shows the session and confirms step-up with CSRF", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAccountPage />);

    expect(await screen.findByText(session.username)).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Current session" })).toBeInTheDocument();
    expect(screen.getByText("Authenticated")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Authentication code"), {
      target: { value: "123456" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Confirm step-up" }));

    await vi.waitFor(() => expect(state.csrf.stepUp).toBe("csrf-token"));
    await vi.waitFor(() => {
      const expected = new Date(state.stepUpAuthenticatedAt).toLocaleString("en");
      expect(screen.getAllByText(expected).length).toBeGreaterThan(0);
    });
  });

  it("says an account has no second factor rather than leaving the field blank", async () => {
    // ADR 0028: an account created by `bootstrap-admin` has a password only.
    state.authenticated = true;
    adminServer.use(
      http.get("/api/admin/session", () =>
        HttpResponse.json({ ...session, mfaAuthenticatedAt: null, stepUpAuthenticatedAt: null })
      )
    );
    renderAdmin(<AdminAccountPage />);

    expect(await screen.findAllByText("No second factor")).toHaveLength(2);
  });

  it("generates recovery codes with CSRF and clears them again", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAccountPage />);

    await screen.findByRole("heading", { level: 2, name: "Current session" });
    fireEvent.click(screen.getByRole("button", { name: "Generate recovery codes" }));

    await vi.waitFor(() => expect(state.csrf.recoveryCodes).toBe("csrf-token"));
    expect(await screen.findByRole("heading", { name: "Recovery codes" })).toBeInTheDocument();
    expect(screen.getByText("ABCD-EFGH-JK23")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Clear sensitive value" }));
    expect(screen.queryByText("ABCD-EFGH-JK23")).not.toBeInTheDocument();
  });

  it("logs out with a CSRF token and drops the cached session", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAccountPage />);

    await screen.findByText(session.username);
    fireEvent.click(screen.getByRole("button", { name: "Log out" }));

    await vi.waitFor(() => expect(state.csrf.logout).toBe("csrf-token"));
    await vi.waitFor(() => expect(screen.queryByText(session.username)).not.toBeInTheDocument());
  });
});
