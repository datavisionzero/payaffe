import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import axe from "axe-core";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { NextIntlClientProvider } from "next-intl";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminPage } from "../components/admin-page";
import messages from "../messages/en.json";

const session = {
  status: "authenticated",
  adminAccountId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
  username: "admin@example.test",
  mfaAuthenticatedAt: "2026-07-05T10:00:00Z",
  stepUpAuthenticatedAt: "2026-07-05T10:00:00Z",
  expiresAt: "2026-07-12T10:00:00Z",
  idleExpiresAt: "2026-07-05T22:00:00Z"
};

const payments = {
  payments: [
    {
      paymentId: "03a26b78-c1f3-4230-99d7-9852cedcc181",
      externalReference: "order-123",
      fiatCurrency: "EUR",
      fiatAmountMinor: 1999,
      status: "waiting_for_payment",
      selectedCurrency: "BTC",
      expectedCryptoAmount: "0.00039980",
      paymentAddress: "btc-test-address",
      expiresAt: "2026-07-05T11:00:00Z",
      completedAt: null,
      createdAt: "2026-07-05T10:00:00Z",
      updatedAt: "2026-07-05T10:05:00Z"
    }
  ]
};

const paymentDetail = {
  ...payments.payments[0],
  payerPageId: "payer-page-123",
  lateAcceptanceEndsAt: "2026-07-06T11:00:00Z",
  observedTotal: "0.00039980",
  confirmedEligibleTotal: null
};

const auditLog = {
  entries: [
    {
      eventId: "02ba688c-2a8d-4a20-b783-41a0c2e6a6fa",
      occurredAt: "2026-07-05T10:15:00Z",
      eventType: "admin.audit_log.list",
      outcome: "success",
      actorType: "product_user",
      actorId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
      reasonCode: "audit_log.listed",
      subjectType: "audit_log",
      subjectId: "recent"
    }
  ]
};

const auditLogDetail = {
  ...auditLog.entries[0],
  sourceService: "api",
  sourceIp: "203.0.113.10",
  userAgent: "SensitiveUserAgent/1.0",
  correlationId: "trace-123"
};

const webhookDeliveries = {
  deliveries: [
    {
      webhookEventId: "4c5b4f2a-df57-4804-8a6f-dce55b2e680a",
      paymentId: payments.payments[0].paymentId,
      paymentExternalReference: "order-123",
      eventType: "payment.created",
      eventVersion: "1",
      status: "retry_pending",
      attemptCount: 1,
      lastErrorCode: "http.503",
      nextAttemptAt: "2026-07-05T10:25:00Z",
      occurredAt: "2026-07-05T10:00:00Z",
      createdAt: "2026-07-05T10:00:00Z",
      lastAttemptedAt: "2026-07-05T10:20:00Z",
      lastAttemptResult: "retry_pending",
      lastHttpStatusCode: 503,
      lastSafeErrorCode: "http.503",
      correlationId: "trace-webhook"
    }
  ]
};

let authenticated = false;
let logoutCsrfHeader: string | null = null;
let stepUpAuthenticatedAt = session.stepUpAuthenticatedAt;
let stepUpCsrfHeader: string | null = null;
let auditExportCsrfHeader: string | null = null;
let recoveryCodesCsrfHeader: string | null = null;
let webhookResendCsrfHeader: string | null = null;
let webhookResendEventId: string | null = null;
let credentialCreateCsrfHeader: string | null = null;
let webhookCreateCsrfHeader: string | null = null;
let settlementRequest:
  | { csrf: string | null; body: { expectedVersion?: number; reason?: string } }
  | null = null;
let mfaRequestBody: { challengeId?: string; totpCode?: string; recoveryCode?: string } | null = null;

const server = setupServer(
  http.get("/api/admin/session", () =>
    authenticated
      ? HttpResponse.json({ ...session, stepUpAuthenticatedAt })
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.post("/api/admin/auth/login", async ({ request }) => {
    const body = (await request.json()) as { username?: string; password?: string };
    if (body.username === "admin@example.test" && body.password === "correct-password") {
      return HttpResponse.json({
        status: "mfa_required",
        challengeId: "f53cb01d-5913-4af5-80f9-09f55aa85b30"
      });
    }

    return HttpResponse.json({ title: "Authentication is invalid.", code: "admin_login.invalid" }, { status: 401 });
  }),
  http.post("/api/admin/auth/mfa", async ({ request }) => {
    const body = (await request.json()) as { challengeId?: string; totpCode?: string; recoveryCode?: string };
    mfaRequestBody = body;
    if (
      body.challengeId === "f53cb01d-5913-4af5-80f9-09f55aa85b30" &&
      (body.totpCode === "123456" || body.recoveryCode === "ABCD-EFGH-JK23")
    ) {
      authenticated = true;
      stepUpAuthenticatedAt = session.stepUpAuthenticatedAt;
      return HttpResponse.json({ status: "authenticated" });
    }

    return HttpResponse.json({ title: "Authentication is invalid.", code: "admin_mfa.invalid" }, { status: 401 });
  }),
  http.post("/api/admin/auth/step-up", async ({ request }) => {
    stepUpCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    const body = (await request.json()) as { totpCode?: string };
    if (authenticated && body.totpCode === "123456") {
      stepUpAuthenticatedAt = "2026-07-05T10:20:00Z";
      return HttpResponse.json({
        status: "step_up_authenticated",
        stepUpAuthenticatedAt,
        idleExpiresAt: "2026-07-05T22:20:00Z"
      });
    }

    return HttpResponse.json({ title: "Authentication is invalid.", code: "admin_step_up.invalid" }, { status: 401 });
  }),
  http.get("/api/admin/payments", () =>
    authenticated
      ? HttpResponse.json(payments)
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.get("/api/admin/payments/03a26b78-c1f3-4230-99d7-9852cedcc181", () =>
    authenticated
      ? HttpResponse.json(paymentDetail)
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.get("/api/admin/audit-log", () =>
    authenticated
      ? HttpResponse.json(auditLog)
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.get("/api/admin/audit-log/02ba688c-2a8d-4a20-b783-41a0c2e6a6fa", () =>
    authenticated
      ? HttpResponse.json(auditLogDetail)
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.get("/api/admin/webhook-deliveries", () =>
    authenticated
      ? HttpResponse.json(webhookDeliveries)
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.post("/api/admin/webhook-deliveries/4c5b4f2a-df57-4804-8a6f-dce55b2e680a/resend", ({ request }) => {
    webhookResendCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    webhookResendEventId = "4c5b4f2a-df57-4804-8a6f-dce55b2e680a";
    return authenticated
      ? HttpResponse.json({ status: "resent", deliveryStatus: "delivered" })
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 });
  }),
  http.post("/api/admin/audit-log/export", ({ request }) => {
    auditExportCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    return authenticated
      ? HttpResponse.json({
        exportedAt: "2026-07-05T10:30:00Z",
        entries: [auditLogDetail]
      })
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 });
  }),
  http.post("/api/admin/auth/recovery-codes", ({ request }) => {
    recoveryCodesCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    return authenticated
      ? HttpResponse.json({
        generatedAt: "2026-07-05T10:40:00Z",
        recoveryCodes: ["ABCD-EFGH-JK23", "LMNP-QRST-UV45"]
      })
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 });
  }),
  http.get("/api/admin/observation-health", () =>
    authenticated
      ? HttpResponse.json({
          currencies: [
            {
              supportedCurrency: "BTC",
              providerName: "blockchair",
              status: "available",
              lastSuccessfulAt: "2026-07-05T10:30:00Z",
              lastFailedAt: null,
              lastSafeErrorCode: null
            }
          ]
        })
      : HttpResponse.json({ title: "Authentication is invalid.", code: "admin_session.invalid" }, { status: 401 })
  ),
  http.get("/api/admin/reorg-alerts", () =>
    authenticated ? HttpResponse.json({ alerts: [] }) : HttpResponse.json({}, { status: 401 })
  ),
  http.get("/api/admin/integration-api-credentials", () =>
    authenticated
      ? HttpResponse.json({
          credentials: [
            {
              id: "8a42f394-d565-40ae-8ce4-7df12d9e0323",
              name: "Shop integration",
              status: "active",
              createdAt: "2026-07-05T10:00:00Z",
              lastUsedAt: null,
              updatedAt: "2026-07-05T10:00:00Z",
              version: 1
            }
          ]
        })
      : HttpResponse.json({}, { status: 401 })
  ),
  http.post("/api/admin/integration-api-credentials", async ({ request }) => {
    credentialCreateCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    const body = (await request.json()) as { name?: string };
    return HttpResponse.json(
      {
        credential: {
          id: "8b6c093e-4d15-499e-a156-9d44a1ee835f",
          name: body.name,
          status: "active",
          createdAt: "2026-07-05T11:00:00Z",
          lastUsedAt: null,
          updatedAt: "2026-07-05T11:00:00Z",
          version: 1
        },
        token: "payaffe_test_one_time_token"
      },
      { status: 201 }
    );
  }),
  http.get("/api/admin/webhook-endpoints", () =>
    authenticated ? HttpResponse.json({ endpoints: [] }) : HttpResponse.json({}, { status: 401 })
  ),
  http.post("/api/admin/webhook-endpoints", ({ request }) => {
    webhookCreateCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    return HttpResponse.json(
      {
        endpoint: {
          id: "9c1dcbf5-a43b-4542-8cdb-aa2634aa99aa",
          integrationApiCredentialId: "8a42f394-d565-40ae-8ce4-7df12d9e0323",
          url: "https://partner.example.test/webhooks",
          secretReference: "partner-v1",
          status: "active",
          eventTypes: ["payment.completed"],
          createdAt: "2026-07-05T11:00:00Z",
          updatedAt: "2026-07-05T11:00:00Z",
          version: 1
        }
      },
      { status: 201 }
    );
  }),
  http.get("/api/admin/native-eth-address-pool", () =>
    authenticated
      ? HttpResponse.json({
          unusedCount: 8,
          assignedCount: 2,
          retiredCount: 0,
          lowCapacityThreshold: 5,
          isLowCapacity: false
        })
      : HttpResponse.json({}, { status: 401 })
  ),
  http.get("/api/admin/csrf", () => HttpResponse.json({ csrfToken: "csrf-token" })),
  http.post("/api/admin/auth/logout", ({ request }) => {
    logoutCsrfHeader = request.headers.get("X-CSRF-TOKEN");
    authenticated = false;
    return HttpResponse.json({ status: "logged_out" });
  })
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  authenticated = false;
  logoutCsrfHeader = null;
  stepUpAuthenticatedAt = session.stepUpAuthenticatedAt;
  stepUpCsrfHeader = null;
  auditExportCsrfHeader = null;
  recoveryCodesCsrfHeader = null;
  webhookResendCsrfHeader = null;
  webhookResendEventId = null;
  credentialCreateCsrfHeader = null;
  webhookCreateCsrfHeader = null;
  settlementRequest = null;
  mfaRequestBody = null;
  vi.restoreAllMocks();
  server.resetHandlers();
});
afterAll(() => server.close());

describe("AdminPage", () => {
  it("shows login and completes the MFA sign-in flow", async () => {
    renderAdminPage();

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "correct-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(await screen.findByRole("heading", { name: "Multi-factor authentication" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Authentication code"), {
      target: { value: "123456" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("heading", { name: "Current session" })).toBeInTheDocument();
    expect(screen.getByText("admin@example.test")).toBeInTheDocument();
    expect(screen.getByText("Authenticated")).toBeInTheDocument();
    expect(screen.getByText("Step-up completed")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Recent payments" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "order-123" })).toBeInTheDocument();
    expect(screen.getByText("waiting_for_payment")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "order-123" }));

    expect(await screen.findByRole("heading", { name: "Payment detail" })).toBeInTheDocument();
    expect(await screen.findByText("payer-page-123")).toBeInTheDocument();
    expect(screen.getAllByText("0.00039980")).toHaveLength(2);
    expect(screen.getByText("btc-test-address")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Audit log" })).toBeInTheDocument();
    expect(screen.getByText("admin.audit_log.list")).toBeInTheDocument();
    expect(screen.getByText("audit_log.listed")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Webhook deliveries" })).toBeInTheDocument();
    expect(screen.getAllByText("payment.created").length).toBeGreaterThan(0);
    expect(screen.getByText("http.503")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Resend" }));

    await waitFor(() => {
      expect(webhookResendCsrfHeader).toBe("csrf-token");
    });
    expect(webhookResendEventId).toBe("4c5b4f2a-df57-4804-8a6f-dce55b2e680a");
    expect(await screen.findByText("Resend completed with status delivered.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "admin.audit_log.list" }));

    expect(await screen.findByRole("heading", { name: "Audit log detail" })).toBeInTheDocument();
    expect(await screen.findByText("203.0.113.10")).toBeInTheDocument();
    expect(await screen.findByText("SensitiveUserAgent/1.0")).toBeInTheDocument();
    expect(await screen.findByText("trace-123")).toBeInTheDocument();
    const downloadClick = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined);
    fireEvent.click(screen.getByRole("button", { name: "Export JSON" }));

    await waitFor(() => {
      expect(auditExportCsrfHeader).toBe("csrf-token");
    });
    expect(downloadClick).toHaveBeenCalledOnce();
    expect(await screen.findByText(/Exported 1 event/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Generate recovery codes" }));

    await waitFor(() => {
      expect(recoveryCodesCsrfHeader).toBe("csrf-token");
    });
    expect(await screen.findByRole("heading", { name: "Recovery codes" })).toBeInTheDocument();
    expect(screen.getByText("ABCD-EFGH-JK23")).toBeInTheDocument();
    expect(screen.getByText("LMNP-QRST-UV45")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Authentication code"), {
      target: { value: "123456" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Confirm step-up" }));

    await waitFor(() => {
      expect(stepUpCsrfHeader).toBe("csrf-token");
    });
    await waitFor(() => {
      const expectedStepUpTime = new Date(stepUpAuthenticatedAt).toLocaleString("en");
      expect(screen.getAllByText(expectedStepUpTime).length).toBeGreaterThan(0);
    });
  });

  it("signs in without a second step when the account has no second factor", async () => {
    // What an installation looks like right after `bootstrap-admin`: a password
    // and nothing else (ADR 0028). The MFA form must not appear at all.
    server.use(
      http.post("/api/admin/auth/login", () => {
        authenticated = true;
        return HttpResponse.json({ status: "authenticated", challengeId: null });
      }),
      http.get("/api/admin/session", () =>
        authenticated
          ? HttpResponse.json({
              ...session,
              mfaAuthenticatedAt: null,
              stepUpAuthenticatedAt: null
            })
          : HttpResponse.json(
              { title: "Authentication is invalid.", code: "admin_session.invalid" },
              { status: 401 }
            )
      )
    );

    renderAdminPage();

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "correct-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(await screen.findByRole("heading", { name: "Current session" })).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Multi-factor authentication" })
    ).not.toBeInTheDocument();

    // Says what is true rather than leaving the field blank.
    expect(screen.getAllByText("No second factor")).toHaveLength(2);
  });

  it("can complete MFA with a recovery code", async () => {
    renderAdminPage();

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "correct-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(await screen.findByRole("heading", { name: "Multi-factor authentication" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Use recovery code" }));
    fireEvent.change(screen.getByLabelText("Recovery code"), {
      target: { value: "ABCD-EFGH-JK23" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() => {
      expect(mfaRequestBody).toEqual({
        challengeId: "f53cb01d-5913-4af5-80f9-09f55aa85b30",
        recoveryCode: "ABCD-EFGH-JK23",
        totpCode: null
      });
    });
    expect(await screen.findByRole("heading", { name: "Current session" })).toBeInTheDocument();
  });

  it("logs out with a CSRF token and returns to login state", async () => {
    authenticated = true;
    renderAdminPage();

    expect(await screen.findByRole("heading", { name: "Current session" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Log out" }));

    await waitFor(() => {
      expect(logoutCsrfHeader).toBe("csrf-token");
    });
    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
  });

  it("creates a credential with CSRF and clears its one-time token", async () => {
    authenticated = true;
    renderAdminPage();

    expect(await screen.findByRole("heading", { name: "Integration API Credentials" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Credential name"), {
      target: { value: "New integration" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Create credential" }));

    await waitFor(() => expect(credentialCreateCsrfHeader).toBe("csrf-token"));
    expect(await screen.findByText("payaffe_test_one_time_token")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Clear sensitive value" }));
    expect(screen.queryByText("payaffe_test_one_time_token")).not.toBeInTheDocument();
  });

  it("creates a Webhook Endpoint and validates an Address Pool import", async () => {
    authenticated = true;
    renderAdminPage();

    await screen.findByRole("heading", { name: "Webhook Endpoints" });
    await screen.findByRole("option", { name: "Shop integration" });
    fireEvent.change(screen.getByLabelText("Integration API Credential"), {
      target: { value: "8a42f394-d565-40ae-8ce4-7df12d9e0323" }
    });
    fireEvent.change(screen.getByLabelText("Webhook Endpoint URL"), {
      target: { value: "https://partner.example.test/webhooks" }
    });
    fireEvent.change(screen.getByLabelText("Secret reference"), {
      target: { value: "partner-v1" }
    });
    fireEvent.click(screen.getByRole("checkbox", { name: "payment.completed" }));
    fireEvent.click(screen.getByRole("button", { name: "Create Webhook Endpoint" }));

    await waitFor(() => expect(webhookCreateCsrfHeader).toBe("csrf-token"));
    fireEvent.change(screen.getByLabelText("Payment Addresses"), {
      target: { value: "not-an-ethereum-address" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Import addresses" }));
    expect(await screen.findByText(/unique Native ETH addresses/)).toBeInTheDocument();
  });

  it("settles an eligible Payment with its concurrency version and CSRF", async () => {
    authenticated = true;
    server.use(
      http.get("/api/admin/payments/03a26b78-c1f3-4230-99d7-9852cedcc181", () =>
        HttpResponse.json({
          ...paymentDetail,
          status: "observed",
          version: 3
        })
      ),
      http.post(
        "/api/admin/payments/03a26b78-c1f3-4230-99d7-9852cedcc181/settle",
        async ({ request }) => {
          settlementRequest = {
            csrf: request.headers.get("X-CSRF-TOKEN"),
            body: (await request.json()) as { expectedVersion?: number; reason?: string }
          };
          return HttpResponse.json({
            ...paymentDetail,
            status: "settled",
            settledAt: "2026-07-05T11:00:00Z",
            version: 4
          });
        }
      )
    );
    renderAdminPage();

    fireEvent.click(await screen.findByRole("button", { name: "order-123" }));
    fireEvent.change(await screen.findByLabelText("Settlement reason"), {
      target: { value: "Confirmed with the partner." }
    });
    fireEvent.click(screen.getByRole("button", { name: "Settle Payment" }));

    await waitFor(() => {
      expect(settlementRequest).toEqual({
        csrf: "csrf-token",
        body: { expectedVersion: 3, reason: "Confirmed with the partner." }
      });
    });
  });

  it("has no automated accessibility violations in the login state", async () => {
    const rendered = renderAdminPage();
    await screen.findByRole("heading", { name: "Admin sign-in" });

    const result = await axe.run(rendered.container, {
      rules: { "color-contrast": { enabled: false } }
    });
    expect(result.violations).toEqual([]);
  });
});

function renderAdminPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false
      },
      mutations: {
        retry: false
      }
    }
  });

  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <QueryClientProvider client={queryClient}>
        <AdminPage />
      </QueryClientProvider>
    </NextIntlClientProvider>
  );
}
