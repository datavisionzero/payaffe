import type { QueryClient } from "@tanstack/react-query";
import {
  getAdminAuditLogEntry,
  getAdminNativeEthAddressPool,
  getAdminObservationHealth,
  getAdminPayment,
  getAdminSession,
  listAdminAuditLog,
  listAdminIntegrationApiCredentials,
  listAdminPayments,
  listAdminProjects,
  listAdminAddressHistoryAlerts,
  listAdminReorgAlerts,
  listAdminWebhookDeliveries,
  listAdminWebhookEndpoints
} from "../../lib/admin-api";

// One place for the query keys, because the shell reads the same four lists
// the pages do in order to put counts in the navigation. Sharing the key is
// what keeps that from doubling every request.
export const adminQueries = {
  session: () => ({ queryKey: ["admin-session"], queryFn: getAdminSession, retry: false }),
  projects: () => ({ queryKey: ["admin-projects"], queryFn: listAdminProjects, retry: false }),
  payments: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "payments"],
    queryFn: () => listAdminPayments(projectId),
    retry: false
  }),
  payment: (projectId: string, paymentId: string) => ({
    queryKey: ["admin-project", projectId, "payment", paymentId],
    queryFn: () => getAdminPayment(projectId, paymentId),
    retry: false
  }),
  auditLog: (projectId?: string) => ({
    queryKey: ["admin-audit-log", projectId ?? "all"],
    queryFn: () => listAdminAuditLog(projectId),
    retry: false
  }),
  auditLogEntry: (eventId: string, projectId?: string) => ({
    queryKey: ["admin-audit-log-entry", projectId ?? "all", eventId],
    queryFn: () => getAdminAuditLogEntry(eventId, projectId),
    retry: false
  }),
  webhookDeliveries: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "webhook-deliveries"],
    queryFn: () => listAdminWebhookDeliveries(projectId),
    retry: false
  }),
  webhookEndpoints: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "webhook-endpoints"],
    queryFn: () => listAdminWebhookEndpoints(projectId),
    retry: false
  }),
  credentials: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "integration-api-credentials"],
    queryFn: () => listAdminIntegrationApiCredentials(projectId),
    retry: false
  }),
  addressPool: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "native-eth-address-pool"],
    queryFn: () => getAdminNativeEthAddressPool(projectId),
    retry: false
  }),
  observationHealth: () => ({
    queryKey: ["admin-observation-health"],
    queryFn: getAdminObservationHealth,
    refetchInterval: 30_000,
    retry: false
  }),
  reorgAlerts: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "reorg-alerts"],
    queryFn: () => listAdminReorgAlerts(projectId),
    retry: false
  }),
  addressHistoryAlerts: (projectId: string) => ({
    queryKey: ["admin-project", projectId, "address-history-alerts"],
    queryFn: () => listAdminAddressHistoryAlerts(projectId),
    retry: false
  })
} as const;

export async function invalidateAdminConfiguration(queryClient: QueryClient, projectId: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: adminQueries.credentials(projectId).queryKey }),
    queryClient.invalidateQueries({ queryKey: adminQueries.webhookEndpoints(projectId).queryKey }),
    queryClient.invalidateQueries({ queryKey: adminQueries.addressPool(projectId).queryKey }),
    queryClient.invalidateQueries({ queryKey: adminQueries.auditLog(projectId).queryKey })
  ]);
}
