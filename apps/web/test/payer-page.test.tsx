import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import axe from "axe-core";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { PayerPage } from "../components/payer-page";
import { ThemeProvider } from "../components/theme-provider";

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

const testPayment = {
  ...selectedPayment,
  // The page's own route names the Payment; the URL's shape is the server's.
  payerPageUrl: "https://pay.example.test/pay/fixed-payer-page-id/",
  testMode: true,
  paymentAddress: "tb1qsimulated",
  paymentInstruction: {
    supportedCurrency: "BTC",
    network: "testnet",
    chainId: null,
    amount: "0.0003998",
    amountAtomic: "39980",
    paymentAddress: "tb1qsimulated",
    uri: "bitcoin:?tb=tb1qsimulated&amount=0.0003998",
    expiresAt: "2026-07-04T13:00:00Z"
  }
};

const server = setupServer(
  http.get("/api/payer/payments/fixed-payer-page-id", () => HttpResponse.json(pendingPayment)),
  http.post("/api/payer/payments/fixed-payer-page-id/currency-selection", () =>
    HttpResponse.json(selectedPayment)
  )
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  vi.useRealTimers();
  server.resetHandlers();
});
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
    expect(
      screen.getByRole("link", { name: "Open payment instruction in a compatible wallet" })
    ).toHaveAttribute("href", "bitcoin:btc-test-address?amount=0.00039980");
  });

  it("explains when every Payment Option is unavailable", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({
          ...pendingPayment,
          paymentOptions: pendingPayment.paymentOptions.map((option) => ({
            ...option,
            status: "unavailable",
            unavailableReasonCode: "blockchain_observation.unavailable"
          }))
        })
      )
    );
    renderPayerPage();

    expect(
      await screen.findByText(
        "No Payment Option is currently usable. Please retry later or contact the shop."
      )
    ).toBeInTheDocument();
    expect(screen.getAllByRole("button").every((button) => button.hasAttribute("disabled"))).toBe(
      true
    );
  });

  it("polls until the Payment reaches a final state", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let reads = 0;
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () => {
        reads += 1;
        return HttpResponse.json({
          ...selectedPayment,
          status: reads === 1 ? "waiting_for_payment" : "completed",
          completedAt: reads === 1 ? null : "2026-07-04T12:30:00Z"
        });
      })
    );
    renderPayerPage();

    expect(await screen.findByText("Waiting for payment")).toBeInTheDocument();
    await vi.advanceTimersByTimeAsync(5000);

    expect(await screen.findByText("Payment completed")).toBeInTheDocument();
    expect(reads).toBe(2);
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

    expect(await screen.findByText("Correlation ID: correlation-123")).toBeInTheDocument();
    // LTC shows the same reason as an option; the selection error is the second.
    expect(screen.getAllByText("An exchange rate is not currently available.")).toHaveLength(2);
    expect(screen.queryByText("Payment details could not be loaded.")).not.toBeInTheDocument();
    expect(screen.queryByText("Provider details that should not be rendered.")).not.toBeInTheDocument();
  });

  it("says a Payment has expired when selecting fails for that reason", async () => {
    server.use(
      http.post("/api/payer/payments/fixed-payer-page-id/currency-selection", () =>
        HttpResponse.json({ title: "Payment has expired.", code: "payment.expired" }, { status: 409 })
      )
    );
    renderPayerPage();
    fireEvent.click(await screen.findByRole("button", { name: "Pay with BTC" }));

    expect(
      await screen.findByText("This Payment has expired. Do not send a transfer.")
    ).toBeInTheDocument();
  });

  it("shows a generic message for a selection failure it does not know", async () => {
    server.use(
      http.post("/api/payer/payments/fixed-payer-page-id/currency-selection", () =>
        HttpResponse.json({ title: "Boom.", code: "something.else" }, { status: 500 })
      )
    );
    renderPayerPage();
    fireEvent.click(await screen.findByRole("button", { name: "Pay with BTC" }));

    expect(
      await screen.findByText("The currency could not be selected. Please try again.")
    ).toBeInTheDocument();
  });

  it("reloads the Payment and drops the error when a currency was already selected", async () => {
    let selected = false;
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json(selected ? selectedPayment : pendingPayment)
      ),
      http.post("/api/payer/payments/fixed-payer-page-id/currency-selection", () => {
        // Selected in another tab between this page's last poll and the click.
        selected = true;
        return HttpResponse.json(
          {
            title: "Payment currency has already been selected.",
            code: "payment.currency_already_selected"
          },
          { status: 409 }
        );
      })
    );
    renderPayerPage();
    fireEvent.click(await screen.findByRole("button", { name: "Pay with ETH" }));

    expect(await screen.findByText("btc-test-address")).toBeInTheDocument();
    expect(screen.getByText("Waiting for payment")).toBeInTheDocument();
    expect(
      screen.queryByText("A currency has already been selected for this Payment.")
    ).not.toBeInTheDocument();
  });

  it("keeps the selection when an older poll resolves after it", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let reads = 0;
    let releaseStalePoll: () => void = () => undefined;
    const stalePoll = new Promise<void>((resolve) => {
      releaseStalePoll = resolve;
    });
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", async () => {
        reads += 1;
        if (reads === 2) {
          await stalePoll;
        }
        return HttpResponse.json(reads >= 3 ? selectedPayment : pendingPayment);
      })
    );
    renderPayerPage();

    const button = await screen.findByRole("button", { name: "Pay with BTC" });
    await vi.advanceTimersByTimeAsync(5000);
    await waitFor(() => expect(reads).toBe(2));
    fireEvent.click(button);
    expect(await screen.findByText("Waiting for payment")).toBeInTheDocument();

    releaseStalePoll();
    await vi.advanceTimersByTimeAsync(100);

    expect(screen.getByText("Waiting for payment")).toBeInTheDocument();
    expect(screen.queryByText("Currency selection pending")).not.toBeInTheDocument();
  });

  it.each(["completed", "expired", "settled"])(
    "shows no payable instruction once the Payment is %s",
    async (status) => {
      server.use(
        http.get("/api/payer/payments/fixed-payer-page-id", () =>
          HttpResponse.json({ ...selectedPayment, status })
        )
      );
      renderPayerPage();

      expect(await screen.findByText(`Payment ${status}`)).toBeInTheDocument();
      expect(screen.queryByLabelText("QR code for the payment instruction")).not.toBeInTheDocument();
      expect(
        screen.queryByRole("link", { name: "Open payment instruction in a compatible wallet" })
      ).not.toBeInTheDocument();
      expect(screen.queryByText("btc-test-address")).not.toBeInTheDocument();
    }
  );

  it("names an unknown unavailable reason without showing its key", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({
          ...pendingPayment,
          paymentOptions: [
            {
              supportedCurrency: "BTC",
              status: "unavailable",
              unavailableReasonCode: "provider.new_reason"
            }
          ]
        })
      )
    );
    renderPayerPage();

    expect(await screen.findByText("This currency is not currently available.")).toBeInTheDocument();
    expect(screen.queryByText(/unavailableReasons\./)).not.toBeInTheDocument();
  });

  it("uses the server-authored instruction URI for the wallet link", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({
          ...selectedPayment,
          selectedCurrency: "ETH",
          paymentInstruction: {
            supportedCurrency: "ETH",
            network: "testnet",
            chainId: 11155111,
            amount: "0.01",
            amountAtomic: "10000000000000000",
            paymentAddress: "0x0000000000000000000000000000000000000001",
            uri: "ethereum:0x0000000000000000000000000000000000000001@11155111?value=10000000000000000",
            expiresAt: "2026-07-04T13:00:00Z"
          }
        })
      )
    );
    renderPayerPage();

    expect(
      await screen.findByRole("link", { name: "Open payment instruction in a compatible wallet" })
    ).toHaveAttribute(
      "href",
      "ethereum:0x0000000000000000000000000000000000000001@11155111?value=10000000000000000"
    );
  });

  it("offers no simulation and no test mode notice in a live installation", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({ ...selectedPayment, testMode: false })
      )
    );
    renderPayerPage();

    expect(await screen.findByText("Waiting for payment")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Test mode" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "I have paid" })).not.toBeInTheDocument();
  });

  it.each([
    ["I have paid", null],
    ["Simulate an underpayment", "0.0001999"],
    ["Simulate an overpayment", "0.0005997"]
  ])("lets the payer of a test payment press %s", async (label, expectedAmount) => {
    let requestedAmount: unknown = "not requested";
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json(testPayment)
      ),
      http.post(
        "/api/payer/payments/fixed-payer-page-id/simulated-transactions",
        async ({ request }) => {
          requestedAmount = ((await request.json()) as { amount: unknown }).amount;
          return HttpResponse.json(
            {
              paymentId: testPayment.paymentId,
              supportedCurrency: "BTC",
              paymentAddress: testPayment.paymentAddress,
              transactionHash: "a".repeat(64),
              amount: expectedAmount ?? "0.0003998",
              recordedAt: "2026-07-04T12:10:00Z"
            },
            { status: 201 }
          );
        }
      )
    );
    renderPayerPage();

    expect(await screen.findByRole("region", { name: "Test mode" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: label }));

    expect(
      await screen.findByText(
        `Simulated transfer of ${expectedAmount ?? "0.0003998"} BTC sent. The status updates once it is observed.`
      )
    ).toBeInTheDocument();
    expect(requestedAmount).toBe(expectedAmount);
  });

  it("stops offering the simulation once the test payment is complete", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () =>
        HttpResponse.json({ ...testPayment, status: "completed", completedAt: "2026-07-04T12:30:00Z" })
      )
    );
    renderPayerPage();

    expect(await screen.findByText("Payment completed")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Test mode" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "I have paid" })).not.toBeInTheDocument();
  });

  it("has no automated accessibility violations in the test mode payment state", async () => {
    server.use(
      http.get("/api/payer/payments/fixed-payer-page-id", () => HttpResponse.json(testPayment))
    );
    const rendered = renderPayerPage();
    await screen.findByRole("button", { name: "I have paid" });

    const result = await axe.run(rendered.container, {
      rules: { "color-contrast": { enabled: false } }
    });
    expect(result.violations).toEqual([]);
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
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <PayerPage payerPageId="fixed-payer-page-id" />
      </QueryClientProvider>
    </ThemeProvider>
  );
}
