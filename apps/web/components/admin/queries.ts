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
  listAdminReorgAlerts,
  listAdminWebhookDeliveries,
  listAdminWebhookEndpoints
} from "../../lib/admin-api";

// One place for the query keys, because the shell reads the same four lists
// the pages do in order to put counts in the navigation. Sharing the key is
// what keeps that from doubling every request.
export const adminQueries = {
  session: () => ({ queryKey: ["admin-session"], queryFn: getAdminSession, retry: false }),
  payments: () => ({ queryKey: ["admin-payments"], queryFn: listAdminPayments, retry: false }),
  payment: (paymentId: string) => ({
    queryKey: ["admin-payment", paymentId],
    queryFn: () => getAdminPayment(paymentId),
    retry: false
  }),
  auditLog: () => ({ queryKey: ["admin-audit-log"], queryFn: listAdminAuditLog, retry: false }),
  auditLogEntry: (eventId: string) => ({
    queryKey: ["admin-audit-log-entry", eventId],
    queryFn: () => getAdminAuditLogEntry(eventId),
    retry: false
  }),
  webhookDeliveries: () => ({
    queryKey: ["admin-webhook-deliveries"],
    queryFn: listAdminWebhookDeliveries,
    retry: false
  }),
  webhookEndpoints: () => ({
    queryKey: ["admin-webhook-endpoints"],
    queryFn: listAdminWebhookEndpoints,
    retry: false
  }),
  credentials: () => ({
    queryKey: ["admin-integration-api-credentials"],
    queryFn: listAdminIntegrationApiCredentials,
    retry: false
  }),
  addressPool: () => ({
    queryKey: ["admin-native-eth-address-pool"],
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
    queryKey: ["admin-reorg-alerts"],
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
