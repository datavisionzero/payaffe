import type { components } from "./api/generated";
import { ApiError, throwApiError, webApi } from "./api/client";

export type PayerPayment = components["schemas"]["PaymentResponse"];
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

export function formatFiatAmount(currency: string, minorUnits: number | string, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: "currency",
    currency
  }).format(Number(minorUnits) / 100);
}

export function buildPaymentUri(payment: PayerPayment): string {
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
