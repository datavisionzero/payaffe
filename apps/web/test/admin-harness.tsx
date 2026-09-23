import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import type { JsonBodyType } from "msw";
import { setupServer } from "msw/node";
import type React from "react";
import { MemoryRouter } from "react-router";
import { AdminProjectProvider } from "../components/admin/project-context";
import { StepUpProvider } from "../components/admin/step-up";
import { ThemeProvider } from "../components/theme-provider";

export const session = {
  status: "authenticated",
  adminAccountId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
  username: "admin@example.test",
  mfaAuthenticatedAt: "2026-07-05T10:00:00Z",
  stepUpAuthenticatedAt: "2026-07-05T10:00:00Z",
  expiresAt: "2026-07-12T10:00:00Z",
  idleExpiresAt: "2026-07-05T22:00:00Z"
};

export const paymentId = "03a26b78-c1f3-4230-99d7-9852cedcc181";
export const auditEventId = "02ba688c-2a8d-4a20-b783-41a0c2e6a6fa";
export const webhookEventId = "4c5b4f2a-df57-4804-8a6f-dce55b2e680a";
export const credentialId = "8a42f394-d565-40ae-8ce4-7df12d9e0323";
export const projectId = "00000000-0000-0000-0000-000000000001";
export const project = {
  projectId,
  name: "Default Project",
  slug: "default",
  status: "active",
  createdAt: "2026-07-05T10:00:00Z",
  updatedAt: "2026-07-05T10:00:00Z",
  version: 1
};

export const payments = {
  payments: [
    {
      paymentId,
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
    },
    {
      paymentId: "5f0e7c02-2f8f-4a1e-9d0e-1f2b6c0f1a77",
      externalReference: "order-124",
      fiatCurrency: "EUR",
      fiatAmountMinor: 1450,
      status: "completed",
      selectedCurrency: "LTC",
      expectedCryptoAmount: "0.19000000",
      paymentAddress: "ltc-test-address",
      expiresAt: "2026-07-05T12:00:00Z",
      completedAt: "2026-07-05T11:30:00Z",
      createdAt: "2026-07-05T11:00:00Z",
      updatedAt: "2026-07-05T11:30:00Z"
    }
  ]
};

export const paymentDetail = {
  ...payments.payments[0],
  payerPageId: "payer-page-123",
  lateAcceptanceEndsAt: "2026-07-06T11:00:00Z",
  observedTotal: "0.00039980",
  confirmedEligibleTotal: null
};

export const auditLog = {
  entries: [
    {
      eventId: auditEventId,
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

export const auditLogDetail = {
  ...auditLog.entries[0],
  sourceService: "api",
  sourceIp: "203.0.113.10",
  userAgent: "SensitiveUserAgent/1.0",
  correlationId: "trace-123"
};

export const webhookDeliveries = {
  deliveries: [
    {
      webhookEventId,
      paymentId,
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

// What each test wants to assert about the request the UI sent, rather than
// only about what it rendered afterwards.
export const state = {
  authenticated: false,
  testMode: false,
  projects: [{ ...project }],
  stepUpAuthenticatedAt: session.stepUpAuthenticatedAt as string,
  csrf: {} as Record<string, string | null>,
  mfaRequestBody: null as { challengeId?: string; totpCode?: string; recoveryCode?: string } | null,
  settlementRequest: null as
    | { csrf: string | null; body: { projectId?: string; expectedVersion?: number; reason?: string } }
    | null,
  webhookResendEventId: null as string | null,
  addressImportRequest: null as
    | { csrf: string | null; body: { projectId?: string; addresses?: string[] } }
    | null,
  addressPool: {
    unusedCount: 8,
    assignedCount: 2,
    retiredCount: 0,
    lowCapacityThreshold: 5,
    isLowCapacity: false
  },
  observationHealth: [
    {
      supportedCurrency: "BTC",
      providerName: "blockchair",
      status: "available",
      lastSuccessfulAt: "2026-07-05T10:30:00Z",
      lastFailedAt: null as string | null,
      lastSafeErrorCode: null as string | null
    }
  ],
  reorgAlerts: [] as JsonBodyType[],
  addressHistoryAlerts: [] as JsonBodyType[]
};

export function resetAdminState() {
  window.localStorage?.clear();
  state.authenticated = false;
  state.testMode = false;
  state.projects = [{ ...project }];
  state.stepUpAuthenticatedAt = session.stepUpAuthenticatedAt;
  state.csrf = {};
  state.mfaRequestBody = null;
  state.settlementRequest = null;
  state.webhookResendEventId = null;
  state.addressImportRequest = null;
  state.addressPool = {
    unusedCount: 8,
    assignedCount: 2,
    retiredCount: 0,
    lowCapacityThreshold: 5,
    isLowCapacity: false
  };
  state.observationHealth = [
    {
      supportedCurrency: "BTC",
      providerName: "blockchair",
      status: "available",
      lastSuccessfulAt: "2026-07-05T10:30:00Z",
      lastFailedAt: null,
      lastSafeErrorCode: null
    }
  ];
  state.reorgAlerts = [];
  state.addressHistoryAlerts = [];
}

const unauthorized = () =>
  HttpResponse.json(
    { title: "Authentication is invalid.", code: "admin_session.invalid" },
    { status: 401 }
  );

function guarded(body: JsonBodyType) {
  return state.authenticated ? HttpResponse.json(body) : unauthorized();
}

// What a step-up-protected endpoint answers until the session has stepped up.
export const stepUpRequired = () =>
  HttpResponse.json(
    { title: "Step-up authentication is required.", code: "admin_step_up.required" },
    { status: 403 }
  );

export function hasSteppedUp() {
  return state.stepUpAuthenticatedAt !== session.stepUpAuthenticatedAt;
}

export async function completeStepUp(totpCode = "123456") {
  const dialog = await screen.findByRole("dialog", { name: "Confirm step-up" });
  fireEvent.change(within(dialog).getByLabelText("Authentication code"), {
    target: { value: totpCode }
  });
  fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));
  return dialog;
}

function capture(name: string, request: Request) {
  state.csrf[name] = request.headers.get("X-CSRF-TOKEN");
}

export const adminServer = setupServer(
  http.get("/api/admin/projects", () =>
    guarded({
      projects: state.projects
    })
  ),
  http.post("/api/admin/projects", async ({ request }) => {
    if (!state.authenticated) {
      return unauthorized();
    }
    capture("createProject", request);
    const body = (await request.json()) as { name: string; slug: string };
    const created = {
      projectId: "2a683f48-acde-4b22-bc3f-468d283296af",
      name: body.name,
      slug: body.slug,
      status: "active",
      createdAt: "2026-07-05T12:00:00Z",
      updatedAt: "2026-07-05T12:00:00Z",
      version: 1
    };
    state.projects.push(created);
    return HttpResponse.json({ project: created }, { status: 201 });
  }),
  http.post("/api/admin/projects/:projectId/status", async ({ params, request }) => {
    if (!state.authenticated) {
      return unauthorized();
    }
    capture("changeProjectStatus", request);
    const body = (await request.json()) as { expectedVersion: number; status: string };
    const index = state.projects.findIndex((candidate) => candidate.projectId === params.projectId);
    const current = state.projects[index];
    const updated = {
      ...current,
      status: body.status,
      updatedAt: "2026-07-05T12:05:00Z",
      version: Number(current.version) + 1
    };
    state.projects[index] = updated;
    return HttpResponse.json({ project: updated });
  }),
  http.get("/api/admin/session", () =>
    guarded({ ...session, stepUpAuthenticatedAt: state.stepUpAuthenticatedAt, testMode: state.testMode })
  ),
  http.post("/api/admin/auth/login", async ({ request }) => {
    const body = (await request.json()) as { username?: string; password?: string };
    if (body.username === "admin@example.test" && body.password === "correct-password") {
      return HttpResponse.json({
        status: "mfa_required",
        challengeId: "f53cb01d-5913-4af5-80f9-09f55aa85b30"
      });
    }

    return HttpResponse.json(
      { title: "Authentication is invalid.", code: "admin_login.invalid" },
      { status: 401 }
    );
  }),
  http.post("/api/admin/auth/mfa", async ({ request }) => {
    const body = (await request.json()) as {
      challengeId?: string;
      totpCode?: string;
      recoveryCode?: string;
    };
    state.mfaRequestBody = body;
    if (
      body.challengeId === "f53cb01d-5913-4af5-80f9-09f55aa85b30" &&
      (body.totpCode === "123456" || body.recoveryCode === "ABCD-EFGH-JK23")
    ) {
      state.authenticated = true;
      state.stepUpAuthenticatedAt = session.stepUpAuthenticatedAt;
      return HttpResponse.json({ status: "authenticated" });
    }

    return HttpResponse.json(
      { title: "Authentication is invalid.", code: "admin_mfa.invalid" },
      { status: 401 }
    );
  }),
  http.post("/api/admin/auth/step-up", async ({ request }) => {
    capture("stepUp", request);
    const body = (await request.json()) as { totpCode?: string };
    if (state.authenticated && body.totpCode === "123456") {
      state.stepUpAuthenticatedAt = "2026-07-05T10:20:00Z";
      return HttpResponse.json({
        status: "step_up_authenticated",
        stepUpAuthenticatedAt: state.stepUpAuthenticatedAt,
        idleExpiresAt: "2026-07-05T22:20:00Z"
      });
    }

    return HttpResponse.json(
      { title: "Authentication is invalid.", code: "admin_step_up.invalid" },
      { status: 401 }
    );
  }),
  http.post("/api/admin/auth/logout", ({ request }) => {
    capture("logout", request);
    state.authenticated = false;
    return HttpResponse.json({ status: "logged_out" });
  }),
  http.post("/api/admin/auth/recovery-codes", ({ request }) => {
    capture("recoveryCodes", request);
    return guarded({
      generatedAt: "2026-07-05T10:40:00Z",
      recoveryCodes: ["ABCD-EFGH-JK23", "LMNP-QRST-UV45"]
    });
  }),
  http.get("/api/admin/payments", () => guarded(payments)),
  http.get(`/api/admin/payments/${paymentId}`, () => guarded(paymentDetail)),
  http.get("/api/admin/audit-log", () => guarded(auditLog)),
  http.get(`/api/admin/audit-log/${auditEventId}`, () => guarded(auditLogDetail)),
  http.post("/api/admin/audit-log/export", ({ request }) => {
    capture("auditExport", request);
    return guarded({ exportedAt: "2026-07-05T10:30:00Z", entries: [auditLogDetail] });
  }),
  http.get("/api/admin/webhook-deliveries", () => guarded(webhookDeliveries)),
  http.post(`/api/admin/webhook-deliveries/${webhookEventId}/resend`, ({ request }) => {
    capture("webhookResend", request);
    state.webhookResendEventId = webhookEventId;
    return guarded({ status: "resent", deliveryStatus: "delivered" });
  }),
  http.get("/api/admin/observation-health", () => guarded({ currencies: state.observationHealth })),
  http.get("/api/admin/reorg-alerts", () => guarded({ alerts: state.reorgAlerts })),
  http.get("/api/admin/address-history-alerts", () =>
    guarded({ alerts: state.addressHistoryAlerts })
  ),
  http.get("/api/admin/integration-api-credentials", () =>
    guarded({
      credentials: [
        {
          id: credentialId,
          name: "Shop integration",
          status: "active",
          createdAt: "2026-07-05T10:00:00Z",
          lastUsedAt: null,
          updatedAt: "2026-07-05T10:00:00Z",
          version: 1
        }
      ]
    })
  ),
  http.post("/api/admin/integration-api-credentials", async ({ request }) => {
    capture("credentialCreate", request);
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
  http.get("/api/admin/webhook-endpoints", () => guarded({ endpoints: [] })),
  http.post("/api/admin/webhook-endpoints", ({ request }) => {
    capture("webhookCreate", request);
    return HttpResponse.json(
      {
        endpoint: {
          id: "9c1dcbf5-a43b-4542-8cdb-aa2634aa99aa",
          integrationApiCredentialId: credentialId,
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
  http.get("/api/admin/native-eth-address-pool", () => guarded(state.addressPool)),
  http.post("/api/admin/native-eth-address-pool/import", async ({ request }) => {
    capture("addressImport", request);
    const body = (await request.json()) as { projectId?: string; addresses?: string[] };
    state.addressImportRequest = {
      csrf: request.headers.get("X-CSRF-TOKEN"),
      body
    };
    state.addressPool = {
      ...state.addressPool,
      unusedCount: state.addressPool.unusedCount + (body.addresses?.length ?? 0)
    };
    return HttpResponse.json(
      {
        importId: "596de3ac-7fab-41f8-b2a4-5d40d8e70e7e",
        importedCount: body.addresses?.length ?? 0,
        summary: state.addressPool
      },
      { status: 201 }
    );
  }),
  http.get("/api/admin/csrf", () => HttpResponse.json({ csrfToken: "csrf-token" }))
);

export function renderAdmin(ui: React.ReactNode, { initialEntries }: { initialEntries?: string[] } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  const rendered = render(
    <ThemeProvider>
      <MemoryRouter initialEntries={initialEntries}>
        <QueryClientProvider client={queryClient}>
          <AdminProjectProvider project={project}>
            <StepUpProvider>{ui}</StepUpProvider>
          </AdminProjectProvider>
        </QueryClientProvider>
      </MemoryRouter>
    </ThemeProvider>
  );
  return { ...rendered, queryClient };
}
