import { fireEvent, screen } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminAddressesPage } from "../components/admin/addresses-page";
import { AdminIntegrationsPage } from "../components/admin/integrations-page";
import { AdminWebhooksPage } from "../components/admin/webhooks-page";
import { adminServer, credentialId, renderAdmin, resetAdminState, state } from "./admin-harness";

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
  usePathname: () => "/admin/webhooks",
  useSearchParams: () => new URLSearchParams(nav.search)
}));

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  nav.search = "";
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminIntegrationsPage", () => {
  it("creates a credential with CSRF and clears its one-time token", async () => {
    state.authenticated = true;
    renderAdmin(<AdminIntegrationsPage />);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Integration API Credentials" })
    ).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Credential name"), {
      target: { value: "New integration" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Create credential" }));

    await vi.waitFor(() => expect(state.csrf.credentialCreate).toBe("csrf-token"));
    expect(await screen.findByText("payaffe_test_one_time_token")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Clear sensitive value" }));
    expect(screen.queryByText("payaffe_test_one_time_token")).not.toBeInTheDocument();
  });
});

describe("AdminWebhooksPage", () => {
  it("creates a Webhook Endpoint from the endpoints view", async () => {
    state.authenticated = true;
    renderAdmin(<AdminWebhooksPage />);

    await screen.findByRole("heading", { level: 2, name: "Webhook Endpoints" });
    await screen.findByRole("option", { name: "Shop integration" });
    fireEvent.change(screen.getByLabelText("Integration API Credential"), {
      target: { value: credentialId }
    });
    fireEvent.change(screen.getByLabelText("Webhook Endpoint URL"), {
      target: { value: "https://partner.example.test/webhooks" }
    });
    fireEvent.change(screen.getByLabelText("Secret reference"), {
      target: { value: "partner-v1" }
    });
    fireEvent.click(screen.getByRole("checkbox", { name: "payment.completed" }));
    fireEvent.click(screen.getByRole("button", { name: "Create Webhook Endpoint" }));

    await vi.waitFor(() => expect(state.csrf.webhookCreate).toBe("csrf-token"));
  });

  it("marks the selected view and resends a failed Delivery from it", async () => {
    state.authenticated = true;
    vi.spyOn(window, "confirm").mockReturnValue(true);
    nav.search = "?view=deliveries";
    renderAdmin(<AdminWebhooksPage />);

    expect(screen.getByRole("link", { name: "Deliveries" })).toHaveAttribute(
      "aria-current",
      "page"
    );
    expect(screen.getByRole("link", { name: "Endpoints" })).not.toHaveAttribute("aria-current");
    expect(
      await screen.findByRole("heading", { level: 2, name: "Webhook deliveries" })
    ).toBeInTheDocument();
    expect(await screen.findByText("http.503")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Resend" }));

    await vi.waitFor(() => expect(state.csrf.webhookResend).toBe("csrf-token"));
    expect(state.webhookResendEventId).toBe("4c5b4f2a-df57-4804-8a6f-dce55b2e680a");
    expect(await screen.findByText("Resend completed with status delivered.")).toBeInTheDocument();
  });

  it("does not resend a Delivery when confirmation is declined", async () => {
    state.authenticated = true;
    nav.search = "?view=deliveries";
    vi.spyOn(window, "confirm").mockReturnValue(false);
    renderAdmin(<AdminWebhooksPage />);

    fireEvent.click(await screen.findByRole("button", { name: "Resend" }));

    expect(window.confirm).toHaveBeenCalledWith(
      "Resend payment.created for order-123?"
    );
    expect(state.webhookResendEventId).toBeNull();
  });

  it("shows the endpoints view by default", async () => {
    state.authenticated = true;
    renderAdmin(<AdminWebhooksPage />);

    expect(screen.getByRole("link", { name: "Endpoints" })).toHaveAttribute("aria-current", "page");
    expect(
      screen.queryByRole("heading", { level: 2, name: "Webhook deliveries" })
    ).not.toBeInTheDocument();
  });
});

describe("AdminAddressesPage", () => {
  it("imports valid Native ETH addresses with Project scope and CSRF", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAddressesPage />);

    await screen.findByRole("heading", { level: 1, name: "Native ETH Address Pool" });
    fireEvent.change(screen.getByLabelText("Payment Addresses"), {
      target: { value: "0x1111111111111111111111111111111111111111" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Import addresses" }));

    await vi.waitFor(() => {
      expect(state.addressImportRequest).toEqual({
        csrf: "csrf-token",
        body: {
          projectId: "00000000-0000-0000-0000-000000000001",
          addresses: ["0x1111111111111111111111111111111111111111"]
        }
      });
    });
  });

  it("rejects an import that is not a Native ETH address", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAddressesPage />);

    await screen.findByRole("heading", { level: 1, name: "Native ETH Address Pool" });
    fireEvent.change(screen.getByLabelText("Payment Addresses"), {
      target: { value: "not-an-ethereum-address" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Import addresses" }));

    expect(await screen.findByText(/unique Native ETH addresses/)).toBeInTheDocument();
  });

  it("summarises pool capacity", async () => {
    state.authenticated = true;
    renderAdmin(<AdminAddressesPage />);

    expect(await screen.findByText("Available")).toBeInTheDocument();
    expect(screen.getByText("8")).toBeInTheDocument();
  });
});
