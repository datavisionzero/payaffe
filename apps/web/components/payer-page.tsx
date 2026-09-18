"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { ThemeSelect } from "./theme-select";
import { createText } from "../lib/text";
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
const t = createText({
  title: "Complete payment",
  loading: "Loading payment",
  notFound: "Payment was not found.",
  unexpectedError: "Payment details could not be loaded.",
  correlationId: "Correlation ID: {correlationId}",
  amountDue: "Amount due",
  expiresAt: "Expires",
  status: "Status",
  selectCurrency: "Select currency",
  selectCurrencyDescription: "Choose how you want to pay.",
  payInstruction: "Payment instruction",
  expectedAmount: "Expected amount",
  paymentAddress: "Payment address",
  qrCode: "QR code for the payment instruction",
  openWallet: "Open payment instruction in a compatible wallet",
  selecting: "Selecting",
  payWith: "Pay with {currency}",
  currencyUnavailable: "{currency} unavailable",
  noUsableOptions: "No Payment Option is currently usable. Please retry later or contact the shop.",
  observationDelay: "Blockchain Observation can be delayed. Keep this page open; the status updates automatically.",
  returnToShop: "Return to shop",
  returnManual: "This link is shown after completion. You will not be redirected automatically.",
  "unavailableReasons.exchange_rate.unavailable": "An exchange rate is not currently available.",
  "unavailableReasons.payment_address.unavailable": "A Payment Address is not currently available.",
  "unavailableReasons.blockchain_observation.unavailable": "Blockchain Observation is not currently available.",
  "statusDescriptions.pending_currency_selection": "Choose an available Supported Currency to continue.",
  "statusDescriptions.waiting_for_payment": "Send the exact amount to the Payment Address below.",
  "statusDescriptions.observed": "The transfer was observed and is waiting for the required confirmations.",
  "statusDescriptions.completed": "The Payment is complete. No further transfer is needed.",
  "statusDescriptions.expired": "The regular payment window has expired. Do not send a new transfer.",
  "statusDescriptions.settled": "An Admin has resolved this Payment.",
  "statusDescriptions.unknown": "The Payment status is not recognized. Refresh or contact the shop.",
  "statuses.pending_currency_selection": "Currency selection pending",
  "statuses.waiting_for_payment": "Waiting for payment",
  "statuses.observed": "Payment observed",
  "statuses.completed": "Payment completed",
  "statuses.expired": "Payment expired",
  "statuses.settled": "Payment settled"
});

export function PayerPage({ payerPageId }: { payerPageId: string }) {
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
  const selected = payment.selectedCurrency !== null && payment.paymentAddress !== null;
  const returnUrl = safeReturnUrl(payment.returnUrl);
  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
      <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
        <dl className="grid gap-4 sm:grid-cols-3">
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("amountDue")}</dt>
            <dd className="mt-1 text-xl font-semibold">
              {formatFiatAmount(payment.fiatCurrency, payment.fiatAmountMinor, "en")}
            </dd>
          </div>
          <div>
            <dt className="text-sm text-[var(--muted-foreground)]">{t("expiresAt")}</dt>
            <dd className="mt-1 text-base">
              {new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" }).format(
                new Date(payment.expiresAt)
              )}
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
  const paymentUri = buildPaymentUri(payment);
  return (
    <div className="mt-8 border-t border-[var(--border)] pt-6">
      <h2 className="text-lg font-semibold">{t("payInstruction")}</h2>
      <div className="mt-5 grid gap-5 sm:grid-cols-[180px_minmax(0,1fr)]">
        <a
          aria-label={t("openWallet")}
          className="flex h-[180px] w-[180px] items-center justify-center rounded-md border border-[var(--border)] bg-[#fff] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--ring)]"
          href={paymentUri}
        >
          <QRCodeSVG aria-label={t("qrCode")} size={144} value={paymentUri} />
        </a>
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
