import type { components } from "./api/generated";
import { ApiError, throwApiError, webApi } from "./api/client";

export type PayerPayment = components["schemas"]["PaymentResponse"];
export type SimulatedTransaction = components["schemas"]["SimulatedTransactionResponse"];
export { ApiError as PayerApiError };

export async function getPayerPayment(payerPageId: string): Promise<PayerPayment> {
  const { data, error, response } = await webApi.GET("/api/payer/payments/{payerPageId}", {
    params: { path: { payerPageId } }
  });
  if (!data) {
    throwApiError(response, error);
  }

  return data;
}

export async function selectPayerPaymentCurrency(
  payerPageId: string,
  supportedCurrency: string
): Promise<PayerPayment> {
  const { data, error, response } = await webApi.POST(
    "/api/payer/payments/{payerPageId}/currency-selection",
    {
      params: { path: { payerPageId } },
      body: { supportedCurrency }
    }
  );
  if (!data) {
    throwApiError(response, error);
  }

  return data;
}

/**
 * Test Mode only: records a Simulated Transaction for this Payment. The route
 * does not exist in a live installation, and the page only offers it when the
 * Payment says it is in Test Mode.
 */
export async function recordPayerSimulatedTransaction(
  payerPageId: string,
  amount: string | null
): Promise<SimulatedTransaction> {
  const { data, error, response } = await webApi.POST(
    "/api/payer/payments/{payerPageId}/simulated-transactions",
    {
      params: { path: { payerPageId } },
      body: { amount }
    }
  );
  if (!data) {
    throwApiError(response, error);
  }

  return data;
}

/**
 * The instruction amount scaled by a whole percentage, computed on the
 * atomic-unit integer so no binary floating point touches a crypto amount.
 */
export function scaleInstructionAmount(
  instruction: { supportedCurrency: string; amountAtomic: string },
  percent: number
): string {
  const scale = instruction.supportedCurrency === "ETH" ? 18 : 8;
  const atomic = (BigInt(instruction.amountAtomic) * BigInt(percent)) / 100n;
  const digits = (atomic > 0n ? atomic : 1n).toString().padStart(scale + 1, "0");
  const whole = digits.slice(0, digits.length - scale);
  const fraction = digits.slice(digits.length - scale).replace(/0+$/, "");
  return fraction ? `${whole}.${fraction}` : whole;
}

export function formatFiatAmount(currency: string, minorUnits: number | string, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: "currency",
    currency
  }).format(Number(minorUnits) / 100);
}

export function buildPaymentUri(payment: PayerPayment): string {
  // The server-authored URI carries what the display fields do not, such as
  // the chain ID a native ETH wallet needs to pay on the right network.
  if (payment.paymentInstruction?.uri) {
    return payment.paymentInstruction.uri;
  }

  if (!payment.selectedCurrency || !payment.paymentAddress) {
    return payment.payerPageUrl;
  }

  const amount = payment.expectedCryptoAmount ? `?amount=${payment.expectedCryptoAmount}` : "";
  const scheme = payment.selectedCurrency === "BTC"
    ? "bitcoin"
    : payment.selectedCurrency === "LTC"
      ? "litecoin"
      : "ethereum";
  return `${scheme}:${payment.paymentAddress}${amount}`;
}
