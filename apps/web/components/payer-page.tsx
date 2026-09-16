"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { ThemeSelect } from "./theme-select";
import { useFormatter, useLocale, useTranslations } from "../lib/english";
import {
  buildPaymentUri,
  formatFiatAmount,
  getPayerPayment,
  PayerApiError,
  PayerPayment,
  selectPayerPaymentCurrency
} from "../lib/payer-api";

const finalStatuses = new Set(["completed", "expired", "settled"]);
const statusLabels: Record<string, string> = {
  pending_currency_selection: "statuses.pending_currency_selection",
  waiting_for_payment: "statuses.waiting_for_payment",
  observed: "statuses.observed",
  completed: "statuses.completed",
  expired: "statuses.expired",
  settled: "statuses.settled"
};

export function PayerPage({ payerPageId }: { payerPageId: string }) {
  const t = useTranslations("PayerPage");
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ["payer-payment", payerPageId],
    queryFn: () => getPayerPayment(payerPageId),
    refetchInterval: (queryState) => {
      const status = queryState.state.data?.status;
      return status && finalStatuses.has(status) ? false : 5000;
    }
  });
  const mutation = useMutation({
    mutationFn: (currency: string) => selectPayerPaymentCurrency(payerPageId, currency),
    onSuccess: (payment) => {
      queryClient.setQueryData(["payer-payment", payerPageId], payment);
    }
  });

  return (
    <main className="mx-auto min-h-screen w-full max-w-5xl px-5 py-8 sm:px-8">
      <header className="mb-8 flex items-start justify-between gap-4 border-b border-[var(--border)] pb-5">
        <div>
          <p className="flex items-center gap-2 text-sm font-semibold">
            <span aria-hidden="true" className="size-4 rounded-sm bg-[var(--brand)]" />
            payaffe
          </p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight">{t("title")}</h1>
        </div>
        <ThemeSelect compact />
      </header>

      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
      {query.data ? (
        <PaymentContent
          payment={query.data}
          isSelecting={mutation.isPending}
          onSelect={(currency) => mutation.mutate(currency)}
        />
      ) : null}
    </main>
  );
}

function PaymentContent({
  payment,
  isSelecting,
  onSelect
}: {
  payment: PayerPayment;
  isSelecting: boolean;
  onSelect: (currency: string) => void;
}) {
  const t = useTranslations("PayerPage");
  const format = useFormatter();
  const locale = useLocale();
  const selected = payment.selectedCurrency !== null && payment.paymentAddress !== null;
  const returnUrl = safeReturnUrl(payment.returnUrl);
  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
      <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
        <dl className="grid gap-4 sm:grid-cols-3">
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("amountDue")}</dt>
            <dd className="mt-1 text-xl font-semibold">
              {formatFiatAmount(payment.fiatCurrency, payment.fiatAmountMinor, locale)}
            </dd>
          </div>
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("expiresAt")}</dt>
            <dd className="mt-1 text-base">
              {format.dateTime(new Date(payment.expiresAt), { dateStyle: "medium", timeStyle: "short" })}
            </dd>
          </div>
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("status")}</dt>
            <dd className="mt-1">
              <StatusBadge status={payment.status} />
            </dd>
          </div>
        </dl>

        <StatusMessage payment={payment} />

        {!selected ? (
          <div className="mt-8">
            <h2 className="text-lg font-semibold">{t("selectCurrency")}</h2>
            <p className="mt-1 text-sm text-[var(--muted-foreground)]">{t("selectCurrencyDescription")}</p>
            <div className="mt-4 grid gap-3 sm:grid-cols-3">
              {payment.paymentOptions.map((option) => {
                const isAvailable = option.status === "available";
                return (
                  <div className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)]" key={option.supportedCurrency}>
                    <button
                      className="w-full rounded-md px-4 py-3 text-left font-semibold hover:ring-2 hover:ring-[var(--brand)] disabled:cursor-not-allowed disabled:opacity-60"
                      disabled={isSelecting || !isAvailable}
                      onClick={() => onSelect(option.supportedCurrency)}
                      type="button"
                    >
                      {isSelecting && isAvailable
                        ? t("selecting")
                        : isAvailable
                          ? t("payWith", { currency: option.supportedCurrency })
                          : t("currencyUnavailable", { currency: option.supportedCurrency })}
                    </button>
                    {!isAvailable && option.unavailableReasonCode ? (
                      <p className="px-4 pb-3 text-sm text-[var(--muted-foreground)]">
                        {t(`unavailableReasons.${option.unavailableReasonCode}`)}
                      </p>
                    ) : null}
                  </div>
                );
              })}
            </div>
            {payment.paymentOptions.every((option) => option.status !== "available") ? (
              <p className="mt-4 rounded-md border border-[var(--danger)] p-3 text-sm" role="status">
                {t("noUsableOptions")}
              </p>
            ) : null}
          </div>
        ) : null}

        {selected ? <PaymentInstruction payment={payment} /> : null}

        {returnUrl && (payment.status === "completed" || payment.status === "settled") ? (
          <div className="mt-6 border-t border-[var(--border)] pt-5">
            <a
              className="inline-flex rounded-md bg-[var(--brand)] px-4 py-3 font-semibold text-[var(--brand-foreground)] hover:opacity-90"
              href={returnUrl}
              rel="noopener noreferrer"
            >
              {t("returnToShop")}
            </a>
            <p className="mt-2 text-sm text-[var(--muted-foreground)]">{t("returnManual")}</p>
          </div>
        ) : null}
      </section>

      <aside className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
        <h2 className="text-lg font-semibold">{payment.externalReference}</h2>
        <p className="mt-2 text-sm text-[var(--muted-foreground)]">{payment.paymentId}</p>
      </aside>
    </div>
  );
}

function StatusMessage({ payment }: { payment: PayerPayment }) {
  const t = useTranslations("PayerPage");
  const key = statusLabels[payment.status] ? payment.status : "unknown";
  return (
    <div
      aria-live="polite"
      className="mt-6 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4"
      role="status"
    >
      <p className="font-medium">{t(`statusDescriptions.${key}`)}</p>
      {payment.status === "waiting_for_payment" || payment.status === "observed" ? (
        <p className="mt-2 text-sm text-[var(--muted-foreground)]">{t("observationDelay")}</p>
      ) : null}
    </div>
  );
}

function PaymentInstruction({ payment }: { payment: PayerPayment }) {
  const t = useTranslations("PayerPage");
  const paymentUri = buildPaymentUri(payment);
  return (
    <div className="mt-8 border-t border-[var(--border)] pt-6">
      <h2 className="text-lg font-semibold">{t("payInstruction")}</h2>
      <div className="mt-5 grid gap-5 sm:grid-cols-[180px_minmax(0,1fr)]">
        <div className="flex h-[180px] w-[180px] items-center justify-center rounded-md border border-[var(--border)] bg-[#fff]">
          <QRCodeSVG aria-label={t("qrCode")} size={144} value={paymentUri} />
        </div>
        <dl className="min-w-0 space-y-4">
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("expectedAmount")}</dt>
            <dd className="mt-1 break-words font-mono text-base">
              {payment.expectedCryptoAmount} {payment.selectedCurrency}
            </dd>
          </div>
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("paymentAddress")}</dt>
            <dd className="mt-1 break-all font-mono text-base">{payment.paymentAddress}</dd>
          </div>
        </dl>
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const t = useTranslations("PayerPage");
  const labelKey = statusLabels[status];
  return (
    <span className="inline-flex rounded-md px-3 py-1 text-sm font-medium" data-status={status}>
      {labelKey ? t(labelKey) : status}
    </span>
  );
}

function StateMessage({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5 text-sm">
      {children}
    </div>
  );
}

function ErrorMessage({ error }: { error: Error }) {
  const t = useTranslations("PayerPage");
  if (error instanceof PayerApiError && error.status === 404) {
    return <StateMessage>{t("notFound")}</StateMessage>;
  }

  return (
    <StateMessage>
      <span>{t("unexpectedError")}</span>
      {error instanceof PayerApiError && error.correlationId ? (
        <span className="mt-2 block text-[var(--muted-foreground)]">
          {t("correlationId", { correlationId: error.correlationId })}
        </span>
      ) : null}
    </StateMessage>
  );
}

function safeReturnUrl(value: string | null): string | null {
  if (!value) {
    return null;
  }

  try {
    const parsed = new URL(value);
    return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed.href : null;
  } catch {
    return null;
  }
}
