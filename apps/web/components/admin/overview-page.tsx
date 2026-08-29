"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useTranslations } from "next-intl";
import type { Route } from "next";
import { cn } from "../../lib/utils";
import { EmptyMessage, ErrorMessage, PageHeader, Panel, StateMessage, StatusPill } from "./common";
import { formatDateTime, formatFiatAmount } from "./format";
import { adminQueries } from "./queries";

const RECENT_PAYMENT_COUNT = 5;

export function AdminOverviewPage() {
  const t = useTranslations("AdminPage");

  return (
    <div className="space-y-6">
      <PageHeader description={t("overviewDescription")} title={t("overviewTitle")} />
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
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.observationHealth());
  const currencies = query.data ?? [];
  const available = currencies.filter((health) => health.status === "available").length;

  return (
    <Tile
      attention={currencies.length > 0 && available < currencies.length}
      href="/admin/monitoring"
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
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.reorgAlerts());
  const count = query.data?.length ?? 0;

  return (
    <Tile
      attention={count > 0}
      href="/admin/monitoring"
      linkLabel={t("viewMonitoring")}
      title={t("reorgAlertsTitle")}
      value={query.data ? t("reorgAlertsSummary", { count }) : t("notAvailable")}
    />
  );
}

function WebhookDeliveryTile() {
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.webhookDeliveries());
  const count = query.data?.length ?? 0;

  return (
    <Tile
      attention={count > 0}
      href="/admin/webhooks?view=deliveries"
      linkLabel={t("viewWebhooks")}
      title={t("webhookDeliveriesTitle")}
      value={query.data ? t("webhookDeliveriesSummary", { count }) : t("notAvailable")}
    />
  );
}

function AddressPoolTile() {
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.addressPool());

  return (
    <Tile
      attention={query.data?.isLowCapacity ?? false}
      href="/admin/addresses"
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
  href: Route;
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
      <Link className="mt-4 text-sm font-medium text-[var(--accent)]" href={href}>
        {linkLabel}
      </Link>
    </article>
  );
}

function RecentPayments() {
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.payments());
  const payments = (query.data ?? []).slice(0, RECENT_PAYMENT_COUNT);

  return (
    <Panel>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 className="text-xl font-semibold">{t("paymentsTitle")}</h2>
        <Link className="text-sm font-medium text-[var(--accent)]" href="/admin/payments">
          {t("viewPayments")}
        </Link>
      </div>
      {query.isPending ? <StateMessage>{t("paymentsLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {!query.isPending && !query.isError && payments.length === 0 ? (
        <EmptyMessage>{t("paymentsEmpty")}</EmptyMessage>
      ) : null}
      {payments.length > 0 ? (
        <div className="mt-5 overflow-x-auto">
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
                      className="text-[var(--accent)] hover:text-[var(--accent-strong)]"
                      href={`/admin/payments/${payment.paymentId}`}
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
