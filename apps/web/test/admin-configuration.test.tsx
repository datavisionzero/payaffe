import { fireEvent, screen, within } from "@testing-library/react";
import axe from "axe-core";
import { HttpResponse, http } from "msw";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminAddressesPage } from "../components/admin/addresses-page";
import {
  AdminIntegrationCreatePage,
  AdminIntegrationsPage
} from "../components/admin/integrations-page";
import {
  AdminWebhookEndpointCreatePage,
  AdminWebhooksPage
} from "../components/admin/webhooks-page";
import {
  adminServer,
  completeStepUp,
  credentialId,
  hasSteppedUp,
  renderAdmin,
  resetAdminState,
  state,
  stepUpRequired
} from "./admin-harness";

const credential = {
  id: credentialId,
  name: "Shop integration",
  status: "active",
  createdAt: "2026-07-05T10:00:00Z",
  lastUsedAt: null,
  updatedAt: "2026-07-05T10:00:00Z",
  version: 1
};

const endpoint = {
  id: "9c1dcbf5-a43b-4542-8cdb-aa2634aa99aa",
  integrationApiCredentialId: credentialId,
  url: "https://partner.example.test/webhooks",
  secretReference: "partner-v1",
  status: "active",
  eventTypes: ["payment.completed"],
  createdAt: "2026-07-05T11:00:00Z",
  updatedAt: "2026-07-05T11:00:00Z",
  version: 1
};

function createCredential(name = "New integration") {
  fireEvent.change(screen.getByLabelText("Credential name"), { target: { value: name } });
  fireEvent.click(screen.getByRole("button", { name: "Create credential" }));
}

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
  it("lists credentials with a link to the separate create page", async () => {
    state.authenticated = true;
    renderAdmin(<AdminIntegrationsPage />);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Integration API Credentials" })
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create credential" })).toHaveAttribute(
      "href",
      "/admin/projects/00000000-0000-0000-0000-000000000001/integrations/new"
    );
    expect(screen.queryByLabelText("Credential name")).not.toBeInTheDocument();
  });

  it("creates a credential with CSRF, shows its one-time token, and returns once cleared", async () => {
    state.authenticated = true;
    renderAdmin(<AdminIntegrationCreatePage />);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Create credential" })
    ).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Credential name"), {
      target: { value: "New integration" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Create credential" }));

    await vi.waitFor(() => expect(state.csrf.credentialCreate).toBe("csrf-token"));
    expect(await screen.findByText("payaffe_test_one_time_token")).toBeInTheDocument();
    expect(screen.queryByLabelText("Credential name")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Clear sensitive value" }));
    expect(screen.queryByText("payaffe_test_one_time_token")).not.toBeInTheDocument();
    expect(nav.push).toHaveBeenCalledWith(
      "/admin/projects/00000000-0000-0000-0000-000000000001/integrations"
    );
  });

  it("drops the one-time token from the mutation cache when it is cleared", async () => {
    state.authenticated = true;
    const { queryClient } = renderAdmin(<AdminIntegrationCreatePage />);
    const cachedResults = () =>
      JSON.stringify(queryClient.getMutationCache().getAll().map((mutation) => mutation.state.data));

    await screen.findByRole("heading", { level: 1, name: "Create credential" });
    createCredential();
    expect(await screen.findByText("payaffe_test_one_time_token")).toBeInTheDocument();
    expect(cachedResults()).toContain("payaffe_test_one_time_token");

    fireEvent.click(screen.getByRole("button", { name: "Clear sensitive value" }));

    await vi.waitFor(() => expect(cachedResults()).not.toContain("payaffe_test_one_time_token"));
  });

  it("asks for step-up when required and retries the create", async () => {
    state.authenticated = true;
    let creates = 0;
    adminServer.use(
      http.post("/api/admin/integration-api-credentials", () => {
        creates += 1;
        return hasSteppedUp()
          ? HttpResponse.json(
              {
                credential: { ...credential, name: "New integration" },
                token: "payaffe_test_one_time_token"
              },
              { status: 201 }
            )
          : stepUpRequired();
      })
    );
    renderAdmin(<AdminIntegrationCreatePage />);

    await screen.findByRole("heading", { level: 1, name: "Create credential" });
    createCredential();
    const dialog = await completeStepUp();

    expect(await screen.findByText("payaffe_test_one_time_token")).toBeInTheDocument();
    expect(state.csrf.stepUp).toBe("csrf-token");
    expect(creates).toBe(2);
    expect(dialog).not.toBeInTheDocument();
  });

  it("has no automated accessibility violations in the step-up prompt", async () => {
    state.authenticated = true;
    adminServer.use(http.post("/api/admin/integration-api-credentials", () => stepUpRequired()));
    renderAdmin(<AdminIntegrationCreatePage />);

    await screen.findByRole("heading", { level: 1, name: "Create credential" });
    createCredential();
    const dialog = await screen.findByRole("dialog", { name: "Confirm step-up" });

    const result = await axe.run(dialog, { rules: { "color-contrast": { enabled: false } } });
    expect(result.violations).toEqual([]);
  });

  it("keeps the prompt open when the step-up code is rejected", async () => {
    state.authenticated = true;
    let creates = 0;
    adminServer.use(
      http.post("/api/admin/integration-api-credentials", () => {
        creates += 1;
        return stepUpRequired();
      })
    );
    renderAdmin(<AdminIntegrationCreatePage />);

    await screen.findByRole("heading", { level: 1, name: "Create credential" });
    createCredential();
    const dialog = await completeStepUp("000000");

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("The step-up code is invalid.");
    expect(creates).toBe(1);
  });

  it("does not retry when step-up is cancelled and says why the action failed", async () => {
    state.authenticated = true;
    let creates = 0;
    adminServer.use(
      http.post("/api/admin/integration-api-credentials", () => {
        creates += 1;
        return stepUpRequired();
      })
    );
    renderAdmin(<AdminIntegrationCreatePage />);

    await screen.findByRole("heading", { level: 1, name: "Create credential" });
    createCredential();
    const dialog = await screen.findByRole("dialog", { name: "Confirm step-up" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "This action needs a recent step-up with your authentication code."
    );
    expect(creates).toBe(1);
    expect(state.csrf.stepUp).toBeUndefined();
  });

  it.each([
    [
      "Rotate token",
      "rotate",
      "Rotate the token of Shop integration? The current token stops working immediately."
    ],
    [
      "Disable credential",
      "disable",
      "Disable Shop integration? Requests with its token are rejected from then on."
    ]
  ])("asks before %s and sends nothing when declined", async (button, action, question) => {
    state.authenticated = true;
    const requests: string[] = [];
    adminServer.use(
      http.post(`/api/admin/integration-api-credentials/${credentialId}/${action}`, () => {
        requests.push(action);
        return HttpResponse.json({ credential, token: "payaffe_test_rotated_token" });
      })
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    renderAdmin(<AdminIntegrationsPage />);

    fireEvent.click(await screen.findByRole("button", { name: button }));
    expect(confirm).toHaveBeenCalledWith(question);
    expect(requests).toEqual([]);

    confirm.mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: button }));
    await vi.waitFor(() => expect(requests).toEqual([action]));
  });
});

describe("AdminWebhooksPage", () => {
  it("links to the separate create page from the endpoints view", async () => {
    state.authenticated = true;
    renderAdmin(<AdminWebhooksPage />);

    await screen.findByRole("heading", { level: 2, name: "Webhook Endpoints" });
    expect(screen.getByRole("link", { name: "Create Webhook Endpoint" })).toHaveAttribute(
      "href",
      "/admin/projects/00000000-0000-0000-0000-000000000001/webhooks/new"
    );
    expect(screen.queryByLabelText("Webhook Endpoint URL")).not.toBeInTheDocument();
  });

  it("creates a Webhook Endpoint and returns to the endpoints view", async () => {
    state.authenticated = true;
    renderAdmin(<AdminWebhookEndpointCreatePage />);

    await screen.findByRole("heading", { level: 1, name: "Create Webhook Endpoint" });
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
    await vi.waitFor(() =>
      expect(nav.push).toHaveBeenCalledWith(
        "/admin/projects/00000000-0000-0000-0000-000000000001/webhooks"
      )
    );
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

  it.each([
    [
      "Rotate secret reference",
      "rotate-secret",
      "Rotate the secret reference of https://partner.example.test/webhooks? Deliveries are signed with the new secret from then on."
    ],
    [
      "Disable Webhook Endpoint",
      "disable",
      "Disable the Webhook Endpoint https://partner.example.test/webhooks? Payment events are no longer sent to it."
    ]
  ])("asks before %s and sends nothing when declined", async (button, action, question) => {
    state.authenticated = true;
    const requests: string[] = [];
    adminServer.use(
      http.get("/api/admin/webhook-endpoints", () => HttpResponse.json({ endpoints: [endpoint] })),
      http.post(`/api/admin/webhook-endpoints/${endpoint.id}/${action}`, () => {
        requests.push(action);
        return HttpResponse.json({ endpoint });
      })
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    renderAdmin(<AdminWebhooksPage />);

    fireEvent.click(await screen.findByRole("button", { name: button }));
    expect(confirm).toHaveBeenCalledWith(question);
    expect(requests).toEqual([]);

    confirm.mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: button }));
    await vi.waitFor(() => expect(requests).toEqual([action]));
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
