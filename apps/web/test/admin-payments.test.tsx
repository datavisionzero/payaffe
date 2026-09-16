import { fireEvent, screen, within } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminPaymentDetailPage } from "../components/admin/payment-detail-page";
import { AdminPaymentsPage } from "../components/admin/payments-page";
import { settleAdminPayment } from "../lib/admin-api";
import {
  adminServer,
  paymentDetail,
  paymentId,
  projectId,
  renderAdmin,
  resetAdminState,
  state
} from "./admin-harness";

const nav = vi.hoisted(() => ({ search: "", replace: vi.fn(), push: vi.fn() }));
vi.mock("../lib/navigation", () => ({
  useRouter: () => ({
    back: vi.fn(),
    forward: vi.fn(),
    prefetch: vi.fn(),
    push: nav.push,
    refresh: vi.fn(),
    replace: nav.replace
  }),
  usePathname: () => "/admin/payments",
  useSearchParams: () => new URLSearchParams(nav.search)
}));

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  nav.search = "";
  nav.replace.mockClear();
  nav.push.mockClear();
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminPaymentsPage", () => {
  it("lists Payments and links each one to its own route", async () => {
    state.authenticated = true;
    renderAdmin(<AdminPaymentsPage />);

    expect(screen.getByRole("heading", { level: 1, name: "Payments" })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "order-123" })).toHaveAttribute(
      "href",
      `/admin/projects/${projectId}/payments/${paymentId}`
    );
    expect(screen.getByRole("link", { name: "order-124" })).toBeInTheDocument();
    // The status filter offers the same words, so this asks the table itself.
    expect(within(screen.getByRole("table")).getByText("waiting_for_payment")).toBeInTheDocument();
    expect(screen.getByText("2 payments")).toBeInTheDocument();
  });

  it("narrows the list by status and keeps the filter in the URL", async () => {
    state.authenticated = true;
    renderAdmin(<AdminPaymentsPage />);

    await screen.findByRole("link", { name: "order-123" });
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "completed" } });

    expect(screen.queryByRole("link", { name: "order-123" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "order-124" })).toBeInTheDocument();
    expect(window.location.search).toBe("?status=completed");
  });

  it("narrows the list by external reference", async () => {
    state.authenticated = true;
    renderAdmin(<AdminPaymentsPage />);

    await screen.findByRole("link", { name: "order-123" });
    fireEvent.change(screen.getByLabelText("Search external reference"), {
      target: { value: "order-124" }
    });

    expect(screen.queryByRole("link", { name: "order-123" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "order-124" })).toBeInTheDocument();
  });

  it("says a filter is hiding Payments rather than claiming there are none", async () => {
    state.authenticated = true;
    renderAdmin(<AdminPaymentsPage />);

    await screen.findByRole("link", { name: "order-123" });
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "settled" } });

    expect(screen.getByText("No Payment matches the current filter.")).toBeInTheDocument();
    expect(screen.queryByText("No Payments have been created yet.")).not.toBeInTheDocument();
  });

  it("restores a filter from the URL", async () => {
    state.authenticated = true;
    nav.search = "?status=completed";
    renderAdmin(<AdminPaymentsPage />);

    expect(await screen.findByRole("link", { name: "order-124" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "order-123" })).not.toBeInTheDocument();
  });

  it("shows a safe authorization state when Project access is denied", async () => {
    state.authenticated = true;
    adminServer.use(
      http.get("/api/admin/payments", () =>
        HttpResponse.json(
          { title: "Forbidden.", code: "admin.forbidden", detail: "internal policy detail" },
          { status: 403 }
        )
      )
    );
    renderAdmin(<AdminPaymentsPage />);

    expect(
      await screen.findByText("You are not authorized to perform this action.")
    ).toBeInTheDocument();
    expect(screen.queryByText("internal policy detail")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "order-123" })).not.toBeInTheDocument();
  });
});

describe("AdminPaymentDetailPage", () => {
  it("shows the Payment and refuses Settlement when it is not eligible", async () => {
    state.authenticated = true;
    renderAdmin(<AdminPaymentDetailPage paymentId={paymentId} />);

    expect(await screen.findByRole("heading", { level: 1, name: "order-123" })).toBeInTheDocument();
    expect(await screen.findByText("payer-page-123")).toBeInTheDocument();
    expect(screen.getByText("btc-test-address")).toBeInTheDocument();
    expect(
      screen.getByText("This Payment is not currently eligible for manual Settlement.")
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to payments" })).toHaveAttribute(
      "href",
      `/admin/projects/${projectId}/payments`
    );
  });

  it("settles an eligible Payment with its concurrency version and CSRF", async () => {
    state.authenticated = true;
    adminServer.use(
      http.get(`/api/admin/payments/${paymentId}`, () =>
        HttpResponse.json({ ...paymentDetail, status: "observed", version: 3 })
      ),
      http.post(`/api/admin/payments/${paymentId}/settle`, async ({ request }) => {
        state.settlementRequest = {
          csrf: request.headers.get("X-CSRF-TOKEN"),
          body: (await request.json()) as { expectedVersion?: number; reason?: string }
        };
        return HttpResponse.json({
          ...paymentDetail,
          status: "settled",
          settledAt: "2026-07-05T11:00:00Z",
          version: 4
        });
      })
    );
    renderAdmin(<AdminPaymentDetailPage paymentId={paymentId} />);

    fireEvent.change(await screen.findByLabelText("Settlement reason"), {
      target: { value: "Confirmed with the partner." }
    });
    fireEvent.click(screen.getByRole("button", { name: "Settle Payment" }));

    await vi.waitFor(() => {
      expect(state.settlementRequest).toEqual({
        csrf: "csrf-token",
        body: {
          projectId,
          expectedVersion: 3,
          reason: "Confirmed with the partner."
        }
      });
    });
  });

  it("keeps the initiating Project when selection changes while CSRF is loading", async () => {
    const otherProjectId = "ebc46c0b-c785-47d5-a2b6-5d417374bd79";
    let selectedProjectId = projectId;
    let releaseCsrf!: () => void;
    const csrfGate = new Promise<void>((resolve) => {
      releaseCsrf = resolve;
    });
    adminServer.use(
      http.get("/api/admin/csrf", async () => {
        await csrfGate;
        return HttpResponse.json({ csrfToken: "csrf-token" });
      }),
      http.post(`/api/admin/payments/${paymentId}/settle`, async ({ request }) => {
        state.settlementRequest = {
          csrf: request.headers.get("X-CSRF-TOKEN"),
          body: (await request.json()) as {
            projectId?: string;
            expectedVersion?: number;
            reason?: string;
          }
        };
        return HttpResponse.json({ ...paymentDetail, status: "settled", version: 4 });
      })
    );

    const settlement = settleAdminPayment(selectedProjectId, paymentId, 3, "Captured Project");
    selectedProjectId = otherProjectId;
    releaseCsrf();
    await settlement;

    expect(state.settlementRequest?.body.projectId).toBe(projectId);
  });
});
