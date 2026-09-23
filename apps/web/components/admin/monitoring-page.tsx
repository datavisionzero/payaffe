"use client";

import { useQuery } from "@tanstack/react-query";
import { createText } from "../../lib/text";
import type {
  AdminAddressHistoryAlert,
  AdminObservationHealth,
  AdminReorgAlert
} from "../../lib/admin-api";
import {
  AdminSection,
  EmptyMessage,
  ErrorMessage,
  InfoItem,
  PageHeader,
  StateMessage,
  StatusPill
} from "./common";
import { formatDateTime } from "./format";
import { adminQueries } from "./queries";
import { useAdminProject } from "./project-context";

const t = createText({
  lastFailure: "Last failure",
  lastSuccess: "Last success",
  loading: "Loading",
  monitoringDescription: "Whether Blockchain Observation is working, and which completed Payments a reorganization touched.",
  monitoringTitle: "Monitoring",
  newConfirmations: "New confirmations",
  notAvailable: "Not available",
  observationHealthDescription: "Current safe availability summary for Blockchain Observation.",
  observationHealthTitle: "Observation Health",
  previousConfirmations: "Previous confirmations",
  provider: "Configured provider",
  reorgAlertsDescription: "Warnings for completed Payments affected by a blockchain reorganization.",
  reorgAlertsEmpty: "No Reorg Alerts are available.",
  reorgAlertsTitle: "Reorg Alerts",
  addressHistoryAlertsDescription:
    "Transactions a Payment Address received before its Payment's currency was selected. They do not count toward the Payment.",
  addressHistoryAlertsEmpty: "No Address History Alerts are available.",
  addressHistoryAlertsTitle: "Address History Alerts",
  observedAmount: "Observed amount",
  observedAt: "Observed at",
  currencySelectedAt: "Currency selected at",
  safeErrorCode: "Safe error code",
  transactionHash: "Transaction hash",
  updatedAt: "Updated"
});

export function AdminMonitoringPage() {
  const project = useAdminProject();

  return (
    <div className="space-y-6">
      <PageHeader
        description={`${project.name} · ${t("monitoringDescription")}`}
        title={t("monitoringTitle")}
      />
      <ObservationSection />
      <ReorgAlertSection />
      <AddressHistoryAlertSection />
    </div>
  );
}

function ObservationSection() {
  const query = useQuery(adminQueries.observationHealth());

  return (
    <AdminSection
      description={t("observationHealthDescription")}
      title={`Installation-wide ${t("observationHealthTitle")}`}
    >
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? (
        <div className="mt-5 grid gap-3 sm:grid-cols-3">
          {query.data.map((health: AdminObservationHealth) => (
            <article
              className="rounded-md border border-[var(--border)] p-4"
              key={health.supportedCurrency}
            >
              <div className="flex items-center justify-between gap-3">
                <h3 className="font-semibold">{health.supportedCurrency}</h3>
                <StatusPill status={health.status} />
              </div>
              <dl className="mt-3 grid gap-2 text-sm">
                <InfoItem label={t("provider")} value={health.providerName} />
                <InfoItem
                  label={t("lastSuccess")}
                  value={
                    health.lastSuccessfulAt
                      ? formatDateTime(health.lastSuccessfulAt)
                      : t("notAvailable")
                  }
                />
                <InfoItem
                  label={t("lastFailure")}
                  value={health.lastFailedAt ? formatDateTime(health.lastFailedAt) : t("notAvailable")}
                />
                <InfoItem
                  label={t("safeErrorCode")}
                  value={health.lastSafeErrorCode ?? t("notAvailable")}
                />
              </dl>
            </article>
          ))}
        </div>
      ) : null}
    </AdminSection>
  );
}

function ReorgAlertSection() {
  const project = useAdminProject();
  const query = useQuery(adminQueries.reorgAlerts(project.projectId));

  return (
    <AdminSection description={t("reorgAlertsDescription")} title={t("reorgAlertsTitle")}>
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data?.length === 0 ? <EmptyMessage>{t("reorgAlertsEmpty")}</EmptyMessage> : null}
      {query.data?.map((alert: AdminReorgAlert) => (
        <article className="mt-4 rounded-md border border-[var(--border)] p-4" key={alert.id}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h3 className="font-semibold">
              {alert.supportedCurrency} · {alert.paymentId}
            </h3>
            <StatusPill status={alert.status} />
          </div>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <InfoItem label={t("transactionHash")} value={alert.transactionHash} />
            <InfoItem
              label={t("previousConfirmations")}
              value={String(alert.previousConfirmations)}
            />
            <InfoItem label={t("newConfirmations")} value={String(alert.newConfirmations)} />
            <InfoItem label={t("updatedAt")} value={formatDateTime(alert.updatedAt)} />
          </dl>
        </article>
      ))}
    </AdminSection>
  );
}

function AddressHistoryAlertSection() {
  const project = useAdminProject();
  const query = useQuery(adminQueries.addressHistoryAlerts(project.projectId));

  return (
    <AdminSection
      description={t("addressHistoryAlertsDescription")}
      title={t("addressHistoryAlertsTitle")}
    >
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data?.length === 0 ? (
        <EmptyMessage>{t("addressHistoryAlertsEmpty")}</EmptyMessage>
      ) : null}
      {query.data?.map((alert: AdminAddressHistoryAlert) => (
        <article className="mt-4 rounded-md border border-[var(--border)] p-4" key={alert.id}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h3 className="font-semibold">
              {alert.supportedCurrency} · {alert.paymentId}
            </h3>
            <StatusPill status={alert.status} />
          </div>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <InfoItem label={t("transactionHash")} value={alert.transactionHash} />
            <InfoItem label={t("observedAmount")} value={alert.observedAmount} />
            <InfoItem label={t("observedAt")} value={formatDateTime(alert.observedAt)} />
            <InfoItem
              label={t("currencySelectedAt")}
              value={formatDateTime(alert.currencySelectedAt)}
            />
          </dl>
        </article>
      ))}
    </AdminSection>
  );
}
