import type { QueryClient } from "@tanstack/react-query";
import {
  getAdminAuditLogEntry,
  getAdminNativeEthAddressPool,
  getAdminObservationHealth,
  getAdminPayment,
  getAdminSession,
  getSelectedAdminProjectId,
  listAdminAuditLog,
  listAdminIntegrationApiCredentials,
  listAdminPayments,
  listAdminProjects,
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
  payments: () => ({
    queryKey: ["admin-payments", getSelectedAdminProjectId()],
    queryFn: listAdminPayments,
    retry: false
  }),
  payment: (paymentId: string) => ({
    queryKey: ["admin-payment", getSelectedAdminProjectId(), paymentId],
    queryFn: () => getAdminPayment(paymentId),
    retry: false
  }),
  auditLog: () => ({
    queryKey: ["admin-audit-log", getSelectedAdminProjectId()],
    queryFn: listAdminAuditLog,
    retry: false
  }),
  auditLogEntry: (eventId: string) => ({
    queryKey: ["admin-audit-log-entry", getSelectedAdminProjectId(), eventId],
    queryFn: () => getAdminAuditLogEntry(eventId),
    retry: false
  }),
  webhookDeliveries: () => ({
    queryKey: ["admin-webhook-deliveries", getSelectedAdminProjectId()],
    queryFn: listAdminWebhookDeliveries,
    retry: false
  }),
  webhookEndpoints: () => ({
    queryKey: ["admin-webhook-endpoints", getSelectedAdminProjectId()],
    queryFn: listAdminWebhookEndpoints,
    retry: false
  }),
  credentials: () => ({
    queryKey: ["admin-integration-api-credentials", getSelectedAdminProjectId()],
    queryFn: listAdminIntegrationApiCredentials,
    retry: false
  }),
  addressPool: () => ({
    queryKey: ["admin-native-eth-address-pool", getSelectedAdminProjectId()],
    queryFn: getAdminNativeEthAddressPool,
    retry: false
  }),
  observationHealth: () => ({
    queryKey: ["admin-observation-health"],
    queryFn: getAdminObservationHealth,
    refetchInterval: 30_000,
    retry: false
  }),
  reorgAlerts: () => ({
    queryKey: ["admin-reorg-alerts", getSelectedAdminProjectId()],
    queryFn: listAdminReorgAlerts,
    retry: false
  })
} as const;

export async function invalidateAdminConfiguration(queryClient: QueryClient) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: adminQueries.credentials().queryKey }),
    queryClient.invalidateQueries({ queryKey: adminQueries.webhookEndpoints().queryKey }),
    queryClient.invalidateQueries({ queryKey: adminQueries.addressPool().queryKey }),
    queryClient.invalidateQueries({ queryKey: adminQueries.auditLog().queryKey })
  ]);
}
