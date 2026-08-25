import { screen, within } from "@testing-library/react";
import axe from "axe-core";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminShell } from "../components/admin/admin-shell";
import { AdminOverviewPage } from "../components/admin/overview-page";
import { adminServer, renderAdmin, resetAdminState, state } from "./admin-harness";

const nav = vi.hoisted(() => ({ pathname: "/admin", replace: vi.fn(), push: vi.fn() }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({
    back: vi.fn(),
    forward: vi.fn(),
    prefetch: vi.fn(),
    push: nav.push,
    refresh: vi.fn(),
    replace: nav.replace
  }),
  usePathname: () => nav.pathname,
  useSearchParams: () => new URLSearchParams()
}));

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  nav.pathname = "/admin";
  nav.replace.mockClear();
  nav.push.mockClear();
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminShell", () => {
  it("sends a signed-out admin to the sign-in route instead of rendering the page", async () => {
    renderAdmin(
      <AdminShell>
        <p>Protected content</p>
      </AdminShell>
    );

    await vi.waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/admin/login"));
    expect(screen.queryByText("Protected content")).not.toBeInTheDocument();
  });

  it("frames the page with the section navigation once signed in", async () => {
    state.authenticated = true;
    renderAdmin(
      <AdminShell>
        <p>Protected content</p>
      </AdminShell>
    );

    expect(await screen.findByText("Protected content")).toBeInTheDocument();
    const navigation = screen.getByRole("navigation", { name: "Admin sections" });
    for (const name of [
      "Overview",
      "Payments",
      "Monitoring",
      "Webhooks",
      "Integrations",
      "Addresses",
      "Audit log",
      "Account"
    ]) {
      expect(within(navigation).getByRole("link", { name })).toBeInTheDocument();
    }
    expect(within(navigation).getByRole("link", { name: "Overview" })).toHaveAttribute(
      "aria-current",
      "page"
    );
    expect(screen.getAllByText("admin@example.test").length).toBeGreaterThan(0);
  });

  it("marks the owning section for a nested route", async () => {
    state.authenticated = true;
    nav.pathname = `/admin/payments/${"03a26b78-c1f3-4230-99d7-9852cedcc181"}`;
    renderAdmin(
      <AdminShell>
        <p>Protected content</p>
      </AdminShell>
    );

    await screen.findByText("Protected content");
    const navigation = screen.getByRole("navigation", { name: "Admin sections" });
    expect(within(navigation).getByRole("link", { name: "Payments" })).toHaveAttribute(
      "aria-current",
      "page"
    );
    expect(within(navigation).getByRole("link", { name: "Overview" })).not.toHaveAttribute(
      "aria-current"
    );
  });

  it("counts what needs attention in the navigation", async () => {
    // The point of the counts: a Reorg Alert and a draining Address Pool are
    // visible from any page, not only from the page that lists them.
    state.authenticated = true;
    state.reorgAlerts = [{ id: "a1" }];
    state.addressPool = { ...state.addressPool, unusedCount: 1, isLowCapacity: true };
    state.observationHealth = [
      { ...state.observationHealth[0], status: "unavailable", lastSafeErrorCode: "provider.timeout" }
    ];
    renderAdmin(
      <AdminShell>
        <p>Protected content</p>
      </AdminShell>
    );

    const navigation = await screen.findByRole("navigation", { name: "Admin sections" });
    // One Reorg Alert plus one currency that is not available.
    await vi.waitFor(() =>
      expect(
        within(navigation).getByRole("link", { name: "Monitoring, 2 needs attention" })
      ).toBeInTheDocument()
    );
    expect(
      within(navigation).getByRole("link", { name: "Webhooks, 1 needs attention" })
    ).toBeInTheDocument();
    expect(
      within(navigation).getByRole("link", { name: "Addresses, low capacity" })
    ).toBeInTheDocument();
  });

  it("has no automated accessibility violations around the overview", async () => {
    state.authenticated = true;
    const rendered = renderAdmin(
      <AdminShell>
        <AdminOverviewPage />
      </AdminShell>
    );

    await screen.findByRole("heading", { level: 1, name: "Overview" });
    await screen.findByRole("link", { name: "order-123" });

    const result = await axe.run(rendered.container, {
      rules: { "color-contrast": { enabled: false } }
    });
    expect(result.violations).toEqual([]);
  });
});
