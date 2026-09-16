import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import axe from "axe-core";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { PayerPage } from "../components/payer-page";

const pendingPayment = {
  paymentId: "78d8a09b-1c4a-4b6e-9f94-f8acbd4278f1",
  status: "pending_currency_selection",
  payerPageUrl: "https://pay.example.test/pay/fixed-payer-page-id",
  expiresAt: "2026-07-04T13:00:00Z",
  fiatCurrency: "EUR",
  fiatAmountMinor: 1999,
  externalReference: "order-123",
  selectedCurrency: null,
  expectedCryptoAmount: null,
  paymentAddress: null,
  observedTotal: null,
  completedAt: null,
  settledAt: null,
  returnUrl: null,
  paymentOptions: [
    { supportedCurrency: "BTC", status: "available", unavailableReasonCode: null },
    {
      supportedCurrency: "LTC",
      status: "unavailable",
      unavailableReasonCode: "exchange_rate.unavailable"
    },
    { supportedCurrency: "ETH", status: "available", unavailableReasonCode: null }
  ]
};

const selectedPayment = {
  ...pendingPayment,
  status: "waiting_for_payment",
  selectedCurrency: "BTC",
  expectedCryptoAmount: "0.00039980",
  paymentAddress: "btc-test-address"
};

const server = setupServer(
  http.get("/api/payer/payments/fixed-payer-page-id", () => HttpResponse.json(pendingPayment)),
  http.post("/api/payer/payments/fixed-payer-page-id/currency-selection", () =>
    HttpResponse.json(selectedPayment)
  )
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

describe("PayerPage", () => {
  it("shows payment status and currency options", async () => {
    renderPayerPage();

    expect(await screen.findByText("€19.99")).toBeInTheDocument();
    expect(screen.getByText("Currency selection pending")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pay with BTC" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "LTC unavailable" })).toBeDisabled();
    expect(screen.getByText("An exchange rate is not currently available.")).toBeInTheDocument();
  });

  it("selects a currency and shows the payment instruction", async () => {
    renderPayerPage();

    fireEvent.click(await screen.findByRole("button", { name: "Pay with BTC" }));

    await waitFor(() => {
      expect(screen.getByText("Waiting for payment")).toBeInTheDocument();
    });
    expect(screen.getByText("0.00039980 BTC")).toBeInTheDocument();
    expect(screen.getByText("btc-test-address")).toBeInTheDocument();
    expect(screen.getByLabelText("QR code for the payment instruction")).toBeInTheDocument();
  });

  it.each([
    ["waiting_for_payment", "Send the exact amount to the Payment Address below."],
    ["observed", "The transfer was observed and is waiting for the required confirmations."],
    ["completed", "The Payment is complete. No further transfer is needed."],
    ["expired", "The regular payment window has expired. Do not send a new transfer."],
    ["settled", "An Admin has resolved this Payment."]
  ])("renders the %s state", async (status, description) => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({ ...selectedPayment, status })
      )
    );
    renderPayerPage();

    expect(await screen.findByText(description)).toBeInTheDocument();
  });

  it("shows the Return URL only after completion without redirecting", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({
          ...selectedPayment,
          status: "completed",
          completedAt: "2026-07-04T12:30:00Z",
          returnUrl: "https://shop.example.test/orders/order-123"
        })
      )
    );
    renderPayerPage();

    const link = await screen.findByRole("link", { name: "Return to shop" });
    expect(link).toHaveAttribute("href", "https://shop.example.test/orders/order-123");
    expect(window.location.href).not.toContain("shop.example.test");
  });

  it("renders a safe error with correlation ID when selection fails", async () => {
    server.use(
      http.post("/api/payer/payments/fixed-payer-page-id/currency-selection", () =>
        HttpResponse.json(
          {
            title: "Provider details that should not be rendered.",
            code: "exchange_rate.unavailable",
            correlationId: "correlation-123"
          },
          { status: 409 }
        )
      )
    );
    renderPayerPage();
    fireEvent.click(await screen.findByRole("button", { name: "Pay with BTC" }));

    expect(await screen.findByText("Payment details could not be loaded.")).toBeInTheDocument();
    expect(screen.getByText("Correlation ID: correlation-123")).toBeInTheDocument();
    expect(screen.queryByText("Provider details that should not be rendered.")).not.toBeInTheDocument();
  });

  it("has no automated accessibility violations in the selection state", async () => {
    const rendered = renderPayerPage();
    await screen.findByRole("button", { name: "Pay with BTC" });

    const result = await axe.run(rendered.container, {
      rules: { "color-contrast": { enabled: false } }
    });
    expect(result.violations).toEqual([]);
  });
});

function renderPayerPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false
      },
      mutations: {
        retry: false
      }
    }
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <PayerPage payerPageId="fixed-payer-page-id" />
    </QueryClientProvider>
  );
}
