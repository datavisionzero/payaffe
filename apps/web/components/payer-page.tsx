"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { useEffect } from "react";
import { ThemeSelect } from "./theme-select";
import { createText } from "../lib/text";
import {
  buildPaymentUri,
  formatFiatAmount,
  getPayerPayment,
  PayerApiError,
  PayerPayment,
  recordPayerSimulatedTransaction,
  scaleInstructionAmount,
  selectPayerPaymentCurrency
} from "../lib/payer-api";

const finalStatuses = new Set(["completed", "expired", "settled"]);
const unavailableReasons = new Set([
  "exchange_rate.unavailable",
  "payment_address.unavailable",
  "blockchain_observation.unavailable",
  "project_configuration.disabled"
]);
const selectionErrors = new Set(["payment.expired", "payment.currency_already_selected"]);
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
  selectionFailed: "The currency could not be selected. Please try again.",
  "selectionErrors.payment.expired": "This Payment has expired. Do not send a transfer.",
  "selectionErrors.payment.currency_already_selected": "A currency has already been selected for this Payment.",
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
  "unavailableReasons.project_configuration.disabled": "This currency is not offered for this Payment.",
  "unavailableReasons.unknown": "This currency is not currently available.",
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
  "statuses.settled": "Payment settled",
  testModeTitle: "Test mode",
  testModeDescription:
    "This payment is simulated. Nothing sent to this address reaches anyone, and no real money changes hands.",
  simulateTitle: "Simulate the payment",
  simulateDescription:
    "This installation runs in test mode, so no wallet is needed. Pretend to pay and watch the status change as it would for a real transfer.",
  simulateExact: "I have paid",
  simulateUnderpayment: "Simulate an underpayment",
  simulateOverpayment: "Simulate an overpayment",
  simulating: "Sending",
  simulated: "Simulated transfer of {amount} {currency} sent. The status updates once it is observed."
});

export function PayerPage({ payerPageId }: { payerPageId: string }) {
  const queryClient = useQueryClient();
  const queryKey = ["payer-payment", payerPageId];
  const query = useQuery({
    queryKey,
    queryFn: () => getPayerPayment(payerPageId),
    refetchInterval: (queryState) => {
      const status = queryState.state.data?.status;
      return status && finalStatuses.has(status) ? false : 5000;
    }
  });
  const mutation = useMutation({
    mutationFn: (currency: string) => selectPayerPaymentCurrency(payerPageId, currency),
    onSuccess: async (payment) => {
      // A poll that left before the selection would otherwise land afterwards
      // and put the pending state back.
      await queryClient.cancelQueries({ queryKey });
      queryClient.setQueryData(queryKey, payment);
    },
    onError: async (error) => {
      // A conflict means the Payment moved on (selected elsewhere, expired, or
      // an option changed); show what it is now.
      if (error instanceof PayerApiError && error.status === 409) {
        await queryClient.invalidateQueries({ queryKey });
      }
    }
  });

  // A failed selection only matters while the payer is still choosing.
  const status = query.data?.status;
  const { isError: selectionFailed, reset: resetSelection } = mutation;
  useEffect(() => {
    if (selectionFailed && status && status !== "pending_currency_selection") {
      resetSelection();
    }
  }, [selectionFailed, resetSelection, status]);

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

      {query.data?.testMode ? <TestModeNotice /> : null}

      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {mutation.isError ? <SelectionError error={mutation.error} /> : null}
      {query.data ? (
        <PaymentContent
          payerPageId={payerPageId}
          payment={query.data}
          isSelecting={mutation.isPending}
          onSelect={(currency) => mutation.mutate(currency)}
        />
      ) : null}
    </main>
  );
}

function PaymentContent({
  payerPageId,
  payment,
  isSelecting,
  onSelect
}: {
  payerPageId: string;
  payment: PayerPayment;
  isSelecting: boolean;
  onSelect: (currency: string) => void;
}) {
  const selected = payment.selectedCurrency !== null && payment.paymentAddress !== null;
  // A final Payment takes no transfer, so it must not show anything payable.
  const payable = selected && !finalStatuses.has(payment.status);
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
                        {t(
                          `unavailableReasons.${unavailableReasons.has(option.unavailableReasonCode) ? option.unavailableReasonCode : "unknown"}`
                        )}
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

        {payable ? <PaymentInstruction payment={payment} /> : null}

        {payable && payment.testMode && (payment.status === "waiting_for_payment" || payment.status === "observed") ? (
          <SimulatePayment payerPageId={payerPageId} payment={payment} />
        ) : null}

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

function TestModeNotice() {
  return (
    <section
      aria-labelledby="test-mode-title"
      className="mb-6 rounded-md border-2 border-dashed border-[var(--danger)] bg-[var(--surface-strong)] p-4"
    >
      <h2 className="font-semibold uppercase tracking-wide" id="test-mode-title">
        {t("testModeTitle")}
      </h2>
      <p className="mt-1 text-sm">{t("testModeDescription")}</p>
    </section>
  );
}

function SimulatePayment({ payerPageId, payment }: { payerPageId: string; payment: PayerPayment }) {
  const queryClient = useQueryClient();
  const instruction = payment.paymentInstruction;
  const mutation = useMutation({
    mutationFn: (amount: string | null) => recordPayerSimulatedTransaction(payerPageId, amount),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["payer-payment", payerPageId] })
  });
  const choices: { label: string; amount: string | null }[] = [
    { label: t("simulateExact"), amount: null },
    ...(instruction
      ? [
          { label: t("simulateUnderpayment"), amount: scaleInstructionAmount(instruction, 50) },
          { label: t("simulateOverpayment"), amount: scaleInstructionAmount(instruction, 150) }
        ]
      : [])
  ];

  return (
    <section aria-labelledby="simulate-title" className="mt-8 border-t border-[var(--border)] pt-6">
      <h2 className="text-lg font-semibold" id="simulate-title">
        {t("simulateTitle")}
      </h2>
      <p className="mt-1 text-sm text-[var(--muted-foreground)]">{t("simulateDescription")}</p>
      <div className="mt-4 flex flex-wrap gap-3">
        {choices.map((choice, index) => (
          <button
            className={
              index === 0
                ? "rounded-md bg-[var(--brand)] px-4 py-3 font-semibold text-[var(--brand-foreground)] hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
                : "rounded-md border border-[var(--border)] px-4 py-3 font-semibold hover:ring-2 hover:ring-[var(--brand)] disabled:cursor-not-allowed disabled:opacity-60"
            }
            disabled={mutation.isPending}
            key={choice.label}
            onClick={() => mutation.mutate(choice.amount)}
            type="button"
          >
            {mutation.isPending && mutation.variables === choice.amount ? t("simulating") : choice.label}
          </button>
        ))}
      </div>
      <div aria-live="polite" className="mt-3 text-sm" role="status">
        {mutation.isSuccess
          ? t("simulated", { amount: mutation.data.amount, currency: mutation.data.supportedCurrency })
          : null}
      </div>
      {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
    </section>
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
        <p className="mt-2 text-sm">{t("observationDelay")}</p>
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

function SelectionError({ error }: { error: Error }) {
  const code = error instanceof PayerApiError ? error.code : undefined;
  const message = code && unavailableReasons.has(code)
    ? t(`unavailableReasons.${code}`)
    : code && selectionErrors.has(code)
      ? t(`selectionErrors.${code}`)
      : t("selectionFailed");

  return (
    <StateMessage>
      <span>{message}</span>
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
