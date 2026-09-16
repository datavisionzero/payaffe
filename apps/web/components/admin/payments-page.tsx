"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "../../lib/link";
import { useSearchParams } from "../../lib/navigation";
import { useTranslations } from "../../lib/english";
import { useEffect, useId, useState } from "react";
import type { AdminPaymentSummary } from "../../lib/admin-api";
import { EmptyMessage, ErrorMessage, PageHeader, Panel, StateMessage, StatusPill } from "./common";
import { formatDateTime, formatFiatAmount } from "./format";
import { adminQueries } from "./queries";
import { useAdminProject } from "./project-context";
import { adminProjectPath } from "./project-routes";

const PAYMENT_STATUSES = [
  "pending_currency_selection",
  "waiting_for_payment",
  "observed",
  "completed",
  "expired",
  "settled"
];

export function AdminPaymentsPage() {
  const t = useTranslations("AdminPage");
  const project = useAdminProject();
  const searchParams = useSearchParams();
  const statusFieldId = useId();
  const searchFieldId = useId();
  const query = useQuery(adminQueries.payments(project.projectId));
  const [status, setStatus] = useState(() => searchParams.get("status") ?? "");
  const [search, setSearch] = useState(() => searchParams.get("q") ?? "");

  // The filter lives in the URL so a view can be shared or restored, but the
  // component keeps its own copy: mirroring through history avoids a router
  // navigation on every keystroke.
  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }
    const params = new URLSearchParams();
    if (status) {
      params.set("status", status);
    }
    if (search) {
      params.set("q", search);
    }
    const queryString = params.toString();
    window.history.replaceState(
      null,
      "",
      `${window.location.pathname}${queryString ? `?${queryString}` : ""}`
    );
  }, [search, status]);

  const payments = query.data ?? [];
  const filtered = payments.filter((payment) => matchesFilter(payment, status, search));

  return (
    <div className="space-y-6">
      <PageHeader
        actions={
          <span className="text-sm text-[var(--muted-foreground)]">
            {t("paymentCount", { count: filtered.length })}
          </span>
        }
        description={t("paymentsDescription")}
        title={t("paymentsPageTitle")}
      />

      <Panel>
        <div className="grid gap-4 sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
          <label className="block text-sm font-medium" htmlFor={statusFieldId}>
            <span>{t("filterStatus")}</span>
            <select
              className="mt-2 block h-10 w-full rounded-md border border-[var(--input)] bg-[var(--background)] px-3"
              id={statusFieldId}
              onChange={(event) => setStatus(event.target.value)}
              value={status}
            >
              <option value="">{t("filterStatusAll")}</option>
              {PAYMENT_STATUSES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm font-medium" htmlFor={searchFieldId}>
            <span>{t("filterSearch")}</span>
            <input
              className="mt-2 block h-10 w-full rounded-md border border-[var(--input)] bg-[var(--background)] px-3 text-base outline-none focus:border-[var(--brand)]"
              id={searchFieldId}
              onChange={(event) => setSearch(event.target.value)}
              type="search"
              value={search}
            />
          </label>
        </div>
        {/* The backend returns the most recent Payments and takes no filter, so
            saying which set is being narrowed keeps an empty result from
            reading as "this Payment does not exist". */}
        <p className="mt-3 text-sm text-[var(--muted-foreground)]">
          {t("filterScope", { count: payments.length })}
        </p>
      </Panel>

      {query.isPending ? <StateMessage>{t("paymentsLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}

      {!query.isPending && !query.isError ? (
        <Panel>
          {payments.length === 0 ? <EmptyMessage>{t("paymentsEmpty")}</EmptyMessage> : null}
          {payments.length > 0 && filtered.length === 0 ? (
            <EmptyMessage>{t("paymentsFilteredEmpty")}</EmptyMessage>
          ) : null}
          {filtered.length > 0 ? (
            <PaymentTable payments={filtered} projectId={project.projectId} />
          ) : null}
        </Panel>
      ) : null}
    </div>
  );
}

function PaymentTable({ payments, projectId }: { payments: AdminPaymentSummary[]; projectId: string }) {
  const t = useTranslations("AdminPage");
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[760px] border-collapse text-left text-sm">
        <thead>
          <tr className="border-b border-[var(--border)] text-[var(--muted-foreground)]">
            <th className="py-2 pr-4 font-medium">{t("paymentExternalReference")}</th>
            <th className="py-2 pr-4 font-medium">{t("paymentStatus")}</th>
            <th className="py-2 pr-4 font-medium">{t("paymentAmount")}</th>
            <th className="py-2 pr-4 font-medium">{t("paymentCurrency")}</th>
            <th className="py-2 pr-4 font-medium">{t("paymentCreatedAt")}</th>
            <th className="py-2 font-medium">{t("paymentExpiresAt")}</th>
          </tr>
        </thead>
        <tbody>
          {payments.map((payment) => (
            <tr className="border-b border-[var(--border)] last:border-0" key={payment.paymentId}>
              <td className="max-w-[220px] break-words py-3 pr-4 font-medium">
                <Link
                  className="text-[var(--brand-ink)] hover:text-[var(--brand-ink)]"
                  href={adminProjectPath(projectId, `/payments/${payment.paymentId}`)}
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
              <td className="py-3 pr-4">
                {payment.selectedCurrency ?? t("paymentCurrencyUnselected")}
              </td>
              <td className="py-3 pr-4">{formatDateTime(payment.createdAt)}</td>
              <td className="py-3">{formatDateTime(payment.expiresAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function matchesFilter(payment: AdminPaymentSummary, status: string, search: string): boolean {
  if (status && payment.status !== status) {
    return false;
  }
  if (!search) {
    return true;
  }
  return payment.externalReference.toLowerCase().includes(search.trim().toLowerCase());
}
