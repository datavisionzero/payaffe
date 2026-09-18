import { z } from "zod";
import type { components } from "./api/generated";
import {
  adminMutationHeaders,
  ApiError,
  throwApiError,
  webApi
} from "./api/client";

type Schema<Name extends keyof components["schemas"]> = components["schemas"][Name];

export type AdminLoginStart = Schema<"AdminLoginStartHttpResponse">;
export type AdminSession = Schema<"AdminSessionHttpResponse">;
export type AdminProject = Schema<"AdminProjectReadModel">;
export type AdminPaymentSummary = Schema<"AdminPaymentSummaryReadModel">;
export type AdminPaymentDetail = Schema<"AdminPaymentDetailReadModel">;
export type AdminAuditLogEntry = Schema<"AdminAuditLogEntryReadModel">;
export type AdminAuditLogEntryDetail = Schema<"AdminAuditLogEntryDetailReadModel">;
export type AdminAuditLogExport = Schema<"AdminAuditLogExportHttpResponse">;
export type AdminWebhookDelivery = Schema<"AdminWebhookDeliveryReadModel">;
export type AdminWebhookDeliveryResend = Schema<"AdminWebhookDeliveryResendHttpResponse">;
export type AdminRecoveryCodes = Schema<"AdminRecoveryCodesHttpResponse">;
export type AdminIntegrationApiCredential = Schema<"AdminIntegrationApiCredentialReadModel">;
export type AdminIntegrationApiCredentialSecret =
  Schema<"AdminIntegrationApiCredentialSecretHttpResponse">;
export type AdminWebhookEndpoint = Schema<"AdminWebhookEndpointReadModel">;
export type AdminNativeEthAddressPool = Schema<"AdminNativeEthAddressPoolSummary">;
export type AdminNativeEthAddressPoolImport =
  Schema<"AdminNativeEthAddressPoolImportHttpResponse">;
export type AdminObservationHealth = Schema<"ObservationHealthReadModel">;
export type AdminReorgAlert = Schema<"AdminReorgAlertReadModel">;
export { ApiError as AdminApiError };

export const adminLoginFormSchema = z.object({
  username: z.string().trim().min(1),
  password: z.string().min(1)
});
export const adminMfaFormSchema = z.object({
  totpCode: z.string().trim().regex(/^[0-9]{6}$/)
});
export const adminRecoveryCodeFormSchema = z.object({
  recoveryCode: z.string().trim().min(1).max(64)
});
export const credentialFormSchema = z.object({
  name: z.string().trim().min(1).max(255)
});
export const webhookEndpointFormSchema = z.object({
  integrationApiCredentialId: z.string().uuid(),
  url: z.string().trim().url().max(2048),
  secretReference: z.string().trim().min(1),
  eventTypes: z.array(z.string())
});
export const settlementFormSchema = z.object({
  reason: z.string().trim().min(1).max(500)
});
export const addressPoolImportFormSchema = z.object({
  addresses: z
    .string()
    .transform((value) => value.split(/[\r\n,;]+/).map((item) => item.trim()).filter(Boolean))
    .pipe(z.array(z.string().regex(/^0x[0-9a-fA-F]{40}$/)).min(1).max(10_000))
});
export const projectFormSchema = z.object({
  name: z.string().trim().min(1).max(255),
  slug: z.string().trim().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/).max(100)
});

export type AdminLoginForm = z.infer<typeof adminLoginFormSchema>;
export type AdminMfaForm = z.infer<typeof adminMfaFormSchema>;
export type AdminRecoveryCodeForm = z.infer<typeof adminRecoveryCodeFormSchema>;
export type AdminMfaCompleteCommand = AdminMfaForm | AdminRecoveryCodeForm;
export type CredentialForm = z.infer<typeof credentialFormSchema>;
export type WebhookEndpointForm = z.infer<typeof webhookEndpointFormSchema>;
export type SettlementForm = z.infer<typeof settlementFormSchema>;
export type AddressPoolImportFormInput = z.input<typeof addressPoolImportFormSchema>;
export type ProjectForm = z.infer<typeof projectFormSchema>;

export async function startAdminLogin(command: AdminLoginForm): Promise<AdminLoginStart> {
  const { data, error, response } = await webApi.POST("/api/admin/auth/login", { body: command });
  return requireData(data, error, response);
}

export async function completeAdminMfa(
  challengeId: string,
  command: AdminMfaCompleteCommand
): Promise<void> {
  const { data, error, response } = await webApi.POST("/api/admin/auth/mfa", {
    body: { challengeId, totpCode: null, recoveryCode: null, ...command }
  });
  requireData(data, error, response);
}

export async function getAdminSession(): Promise<AdminSession | null> {
  const { data, error, response } = await webApi.GET("/api/admin/session");
  if (response.status === 401) {
    return null;
  }
  return requireData(data, error, response);
}

export async function listAdminProjects(): Promise<AdminProject[]> {
  const { data, error, response } = await webApi.GET("/api/admin/projects");
  return requireData(data, error, response).projects;
}

export async function createAdminProject(command: ProjectForm): Promise<AdminProject> {
  const { data, error, response } = await webApi.POST("/api/admin/projects", {
    headers: await adminMutationHeaders(),
    body: command
  });
  return requireData(data, error, response).project;
}

export async function changeAdminProjectStatus(
  project: AdminProject,
  status: string
): Promise<AdminProject> {
  const { data, error, response } = await webApi.POST("/api/admin/projects/{projectId}/status", {
    params: { path: { projectId: project.projectId } },
    headers: await adminMutationHeaders(),
    body: { expectedVersion: project.version, status }
  });
  return requireData(data, error, response).project;
}

export async function listAdminPayments(projectId: string): Promise<AdminPaymentSummary[]> {
  const { data, error, response } = await webApi.GET("/api/admin/payments", {
    params: { query: { projectId, limit: 25 } }
  });
  return requireData(data, error, response).payments;
}

export async function getAdminPayment(projectId: string, paymentId: string): Promise<AdminPaymentDetail> {
  const { data, error, response } = await webApi.GET("/api/admin/payments/{paymentId}", {
    params: { path: { paymentId }, query: { projectId } }
  });
  return requireData(data, error, response);
}

export async function settleAdminPayment(
  projectId: string,
  paymentId: string,
  expectedVersion: number | string,
  reason: string
): Promise<AdminPaymentDetail> {
  const { data, error, response } = await webApi.POST("/api/admin/payments/{paymentId}/settle", {
    params: { path: { paymentId } },
    headers: await adminMutationHeaders(),
    body: { projectId, expectedVersion, reason }
  });
  return requireData(data, error, response);
}

export async function stepUpAdmin(command: AdminMfaForm): Promise<void> {
  const { data, error, response } = await webApi.POST("/api/admin/auth/step-up", {
    headers: await adminMutationHeaders(),
    body: command
  });
  requireData(data, error, response);
}

export async function listAdminAuditLog(projectId?: string): Promise<AdminAuditLogEntry[]> {
  const { data, error, response } = await webApi.GET("/api/admin/audit-log", {
    params: { query: { projectId, limit: 25 } }
  });
  return requireData(data, error, response).entries;
}

export async function getAdminAuditLogEntry(
  eventId: string,
  projectId?: string
): Promise<AdminAuditLogEntryDetail> {
  const { data, error, response } = await webApi.GET("/api/admin/audit-log/{eventId}", {
    params: { path: { eventId }, query: { projectId } }
  });
  return requireData(data, error, response);
}

export async function exportAdminAuditLog(projectId?: string): Promise<AdminAuditLogExport> {
  const { data, error, response } = await webApi.POST("/api/admin/audit-log/export", {
    headers: await adminMutationHeaders(),
    body: { projectId: projectId ?? null, limit: 100 }
  });
  return requireData(data, error, response);
}

export async function listAdminWebhookDeliveries(projectId: string): Promise<AdminWebhookDelivery[]> {
  const { data, error, response } = await webApi.GET("/api/admin/webhook-deliveries", {
    params: { query: { projectId, limit: 25 } }
  });
  return requireData(data, error, response).deliveries;
}

export async function resendAdminWebhookDelivery(
  projectId: string,
  eventId: string
): Promise<AdminWebhookDeliveryResend> {
  const { data, error, response } = await webApi.POST(
    "/api/admin/webhook-deliveries/{eventId}/resend",
    {
      params: { path: { eventId }, query: { projectId } },
      headers: await adminMutationHeaders()
    }
  );
  return requireData(data, error, response);
}

export async function generateAdminRecoveryCodes(): Promise<AdminRecoveryCodes> {
  const { data, error, response } = await webApi.POST("/api/admin/auth/recovery-codes", {
    headers: await adminMutationHeaders()
  });
  return requireData(data, error, response);
}

export async function listAdminIntegrationApiCredentials(
  projectId: string
): Promise<AdminIntegrationApiCredential[]> {
  const { data, error, response } = await webApi.GET("/api/admin/integration-api-credentials", {
    params: { query: { projectId } }
  });
  return requireData(data, error, response).credentials;
}

export async function createAdminIntegrationApiCredential(
  projectId: string,
  name: string
): Promise<AdminIntegrationApiCredentialSecret> {
  const { data, error, response } = await webApi.POST("/api/admin/integration-api-credentials", {
    headers: await adminMutationHeaders(),
    body: { projectId, name }
  });
  return requireData(data, error, response);
}

export async function rotateAdminIntegrationApiCredential(
  projectId: string,
  credential: AdminIntegrationApiCredential
): Promise<AdminIntegrationApiCredentialSecret> {
  const { data, error, response } = await webApi.POST(
    "/api/admin/integration-api-credentials/{credentialId}/rotate",
    {
      params: { path: { credentialId: credential.id } },
      headers: await adminMutationHeaders(),
      body: { projectId, expectedVersion: credential.version }
    }
  );
  return requireData(data, error, response);
}

export async function disableAdminIntegrationApiCredential(
  projectId: string,
  credential: AdminIntegrationApiCredential
): Promise<AdminIntegrationApiCredential> {
  const { data, error, response } = await webApi.POST(
    "/api/admin/integration-api-credentials/{credentialId}/disable",
    {
      params: { path: { credentialId: credential.id } },
      headers: await adminMutationHeaders(),
      body: { projectId, expectedVersion: credential.version }
    }
  );
  return requireData(data, error, response).credential;
}

export async function listAdminWebhookEndpoints(projectId: string): Promise<AdminWebhookEndpoint[]> {
  const { data, error, response } = await webApi.GET("/api/admin/webhook-endpoints", {
    params: { query: { projectId } }
  });
  return requireData(data, error, response).endpoints;
}

export async function createAdminWebhookEndpoint(
  projectId: string,
  command: WebhookEndpointForm
): Promise<AdminWebhookEndpoint> {
  const { data, error, response } = await webApi.POST("/api/admin/webhook-endpoints", {
    headers: await adminMutationHeaders(),
    body: { projectId, ...command }
  });
  return requireData(data, error, response).endpoint;
}

export async function updateAdminWebhookEndpoint(
  projectId: string,
  endpoint: AdminWebhookEndpoint,
  command: Pick<WebhookEndpointForm, "url" | "eventTypes">
): Promise<AdminWebhookEndpoint> {
  const { data, error, response } = await webApi.POST(
    "/api/admin/webhook-endpoints/{endpointId}/update",
    {
      params: { path: { endpointId: endpoint.id } },
      headers: await adminMutationHeaders(),
      body: { projectId, expectedVersion: endpoint.version, ...command }
    }
  );
  return requireData(data, error, response).endpoint;
}

export async function rotateAdminWebhookEndpointSecret(
  projectId: string,
  endpoint: AdminWebhookEndpoint,
  secretReference: string
): Promise<AdminWebhookEndpoint> {
  const { data, error, response } = await webApi.POST(
    "/api/admin/webhook-endpoints/{endpointId}/rotate-secret",
    {
      params: { path: { endpointId: endpoint.id } },
      headers: await adminMutationHeaders(),
      body: { projectId, expectedVersion: endpoint.version, secretReference }
    }
  );
  return requireData(data, error, response).endpoint;
}

export async function disableAdminWebhookEndpoint(
  projectId: string,
  endpoint: AdminWebhookEndpoint
): Promise<AdminWebhookEndpoint> {
  const { data, error, response } = await webApi.POST(
    "/api/admin/webhook-endpoints/{endpointId}/disable",
    {
      params: { path: { endpointId: endpoint.id } },
      headers: await adminMutationHeaders(),
      body: { projectId, expectedVersion: endpoint.version }
    }
  );
  return requireData(data, error, response).endpoint;
}

export async function getAdminNativeEthAddressPool(
  projectId: string
): Promise<AdminNativeEthAddressPool> {
  const { data, error, response } = await webApi.GET("/api/admin/native-eth-address-pool", {
    params: { query: { projectId } }
  });
  return requireData(data, error, response);
}

export async function importAdminNativeEthAddressPool(
  projectId: string,
  addresses: string[]
): Promise<AdminNativeEthAddressPoolImport> {
  const { data, error, response } = await webApi.POST("/api/admin/native-eth-address-pool/import", {
    headers: await adminMutationHeaders(),
    body: { projectId, addresses }
  });
  return requireData(data, error, response);
}

export async function getAdminObservationHealth(): Promise<AdminObservationHealth[]> {
  const { data, error, response } = await webApi.GET("/api/admin/observation-health");
  return requireData(data, error, response).currencies;
}

export async function listAdminReorgAlerts(projectId: string): Promise<AdminReorgAlert[]> {
  const { data, error, response } = await webApi.GET("/api/admin/reorg-alerts", {
    params: { query: { projectId, limit: 25 } }
  });
  return requireData(data, error, response).alerts;
}

export async function logoutAdmin(): Promise<void> {
  const { data, error, response } = await webApi.POST("/api/admin/auth/logout", {
    headers: await adminMutationHeaders()
  });
  requireData(data, error, response);
}

function requireData<T>(data: T | undefined, error: unknown, response: Response): T {
  if (data === undefined) {
    throwApiError(response, error);
  }
  return data;
}
