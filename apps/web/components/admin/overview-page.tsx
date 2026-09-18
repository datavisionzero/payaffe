"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "../../lib/link";
import { createText } from "../../lib/text";
import { cn } from "../../lib/utils";
import { EmptyMessage, ErrorMessage, PageHeader, Panel, StateMessage, StatusPill } from "./common";
import { formatDateTime, formatFiatAmount } from "./format";
import { adminQueries } from "./queries";
import { useAdminProject } from "./project-context";
import { adminProjectPath } from "./project-routes";

const RECENT_PAYMENT_COUNT = 5;
const t = createText({
  addressPoolSummary: "{count, plural, one {# unused address} other {# unused addresses}}",
  addressPoolTitle: "Native ETH Address Pool",
  lowCapacity: "Low capacity",
  notAvailable: "Not available",
  observationHealthTitle: "Observation Health",
  observationSummary: "{available} of {total} available",
  overviewTitle: "Overview",
  paymentAmount: "Amount",
  paymentCreatedAt: "Created",
  paymentExternalReference: "External reference",
  paymentStatus: "Status",
  paymentsEmpty: "No Payments have been created yet.",
  paymentsLoading: "Loading payments",
  paymentsTitle: "Recent payments",
  reorgAlertsSummary: "{count, plural, =0 {No open alerts} one {# open alert} other {# open alerts}}",
  reorgAlertsTitle: "Reorg Alerts",
  viewAddresses: "View Address Pool",
  viewMonitoring: "View monitoring",
  viewPayments: "View all payments",
  viewWebhooks: "View webhooks",
  webhookDeliveriesSummary: "{count, plural, =0 {Nothing pending} one {# pending} other {# pending}}",
  webhookDeliveriesTitle: "Webhook deliveries"
});

export function AdminOverviewPage() {
  const project = useAdminProject();

  return (
    <div className="space-y-6">
      <PageHeader
        description={`${project.name} · ${project.status}`}
        title={t("overviewTitle")}
      />
      {/* The four signals an operator would otherwise have to go looking for,
          in the order they cost money if missed. */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <ObservationTile />
        <ReorgAlertTile />
        <WebhookDeliveryTile />
        <AddressPoolTile />
      </div>
      <RecentPayments />
    </div>
  );
}

function ObservationTile() {
  const query = useQuery(adminQueries.observationHealth());
  const currencies = query.data ?? [];
  const project = useAdminProject();
  const available = currencies.filter((health) => health.status === "available").length;

  return (
    <Tile
      attention={currencies.length > 0 && available < currencies.length}
      href={adminProjectPath(project.projectId, "/monitoring")}
      linkLabel={t("viewMonitoring")}
      title={t("observationHealthTitle")}
      value={
        query.data
          ? t("observationSummary", { available, total: currencies.length })
          : t("notAvailable")
      }
    >
      {currencies.length > 0 ? (
        <ul className="mt-3 flex flex-wrap gap-2">
          {currencies.map((health) => (
            <li className="flex items-center gap-1.5 text-sm" key={health.supportedCurrency}>
              <span className="font-medium">{health.supportedCurrency}</span>
              <StatusPill status={health.status} />
            </li>
          ))}
        </ul>
      ) : null}
    </Tile>
  );
}

function ReorgAlertTile() {
  const project = useAdminProject();
  const query = useQuery(adminQueries.reorgAlerts(project.projectId));
  const count = query.data?.length ?? 0;

  return (
    <Tile
      attention={count > 0}
      href={adminProjectPath(project.projectId, "/monitoring")}
      linkLabel={t("viewMonitoring")}
      title={t("reorgAlertsTitle")}
      value={query.data ? t("reorgAlertsSummary", { count }) : t("notAvailable")}
    />
  );
}

function WebhookDeliveryTile() {
  const project = useAdminProject();
  const query = useQuery(adminQueries.webhookDeliveries(project.projectId));
  const count = query.data?.length ?? 0;

  return (
    <Tile
      attention={count > 0}
      href={`${adminProjectPath(project.projectId, "/webhooks")}?view=deliveries`}
      linkLabel={t("viewWebhooks")}
      title={t("webhookDeliveriesTitle")}
      value={query.data ? t("webhookDeliveriesSummary", { count }) : t("notAvailable")}
    />
  );
}

function AddressPoolTile() {
  const project = useAdminProject();
  const query = useQuery(adminQueries.addressPool(project.projectId));

  return (
    <Tile
      attention={query.data?.isLowCapacity ?? false}
      href={adminProjectPath(project.projectId, "/addresses")}
      linkLabel={t("viewAddresses")}
      title={t("addressPoolTitle")}
      value={
        query.data
          ? query.data.isLowCapacity
            ? t("lowCapacity")
            : t("addressPoolSummary", { count: query.data.unusedCount })
          : t("notAvailable")
      }
    />
  );
}

function Tile({
  attention,
  children,
  href,
  linkLabel,
  title,
  value
}: {
  attention: boolean;
  children?: React.ReactNode;
  href: string;
  linkLabel: string;
  title: string;
  value: string;
}) {
  return (
    <article
      className={cn(
        "flex flex-col rounded-md border bg-[var(--surface)] p-5",
        attention ? "border-[var(--danger)]" : "border-[var(--border)]"
      )}
    >
      <h2 className="text-sm font-medium text-[var(--muted-foreground)]">{title}</h2>
      <p
        className={cn(
          "mt-2 text-lg font-semibold",
          attention ? "text-[var(--danger)]" : undefined
        )}
      >
        {value}
      </p>
      <div className="grow">{children}</div>
      <Link className="mt-4 text-sm font-medium text-[var(--brand-ink)]" href={href}>
        {linkLabel}
      </Link>
    </article>
  );
}

function RecentPayments() {
  const project = useAdminProject();
  const query = useQuery(adminQueries.payments(project.projectId));
  const payments = (query.data ?? []).slice(0, RECENT_PAYMENT_COUNT);

  return (
    <Panel>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 className="text-xl font-semibold">{t("paymentsTitle")}</h2>
        <Link
          className="text-sm font-medium text-[var(--brand-ink)]"
          href={adminProjectPath(project.projectId, "/payments")}
        >
          {t("viewPayments")}
        </Link>
      </div>
      {query.isPending ? <StateMessage>{t("paymentsLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {!query.isPending && !query.isError && payments.length === 0 ? (
        <EmptyMessage>{t("paymentsEmpty")}</EmptyMessage>
      ) : null}
      {payments.length > 0 ? (
        <div
          aria-label="Recent payments table"
          className="mt-5 overflow-x-auto"
          role="region"
          tabIndex={0}
        >
          <table className="w-full min-w-[560px] border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-[var(--muted-foreground)]">
                <th className="py-2 pr-4 font-medium">{t("paymentExternalReference")}</th>
                <th className="py-2 pr-4 font-medium">{t("paymentStatus")}</th>
                <th className="py-2 pr-4 font-medium">{t("paymentAmount")}</th>
                <th className="py-2 font-medium">{t("paymentCreatedAt")}</th>
              </tr>
            </thead>
            <tbody>
              {payments.map((payment) => (
                <tr className="border-b border-[var(--border)] last:border-0" key={payment.paymentId}>
                  <td className="max-w-[220px] break-words py-3 pr-4 font-medium">
                    <Link
                      className="text-[var(--brand-ink)] hover:text-[var(--brand-ink)]"
                      href={adminProjectPath(project.projectId, `/payments/${payment.paymentId}`)}
                    >
                      {payment.externalReference}
                    </Link>
                  </td>
                  <td className="py-3 pr-4">
                    <StatusPill status={payment.status} />
                  </td>
                  <td className="py-3 pr-4">
                    {formatFiatAmount(payment.fiatCurrency, payment.fiatAmountMinor)}
                  </td>
                  <td className="py-3">{formatDateTime(payment.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </Panel>
  );
}
