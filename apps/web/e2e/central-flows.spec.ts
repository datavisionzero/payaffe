import { expect, test } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const payment = {
  paymentId: "78d8a09b-1c4a-4b6e-9f94-f8acbd4278f1",
  status: "completed",
  payerPageUrl: "https://pay.example.test/pay/payer-123",
  expiresAt: "2026-07-04T13:00:00Z",
  fiatCurrency: "EUR",
  fiatAmountMinor: 1999,
  externalReference: "order-123",
  selectedCurrency: "BTC",
  expectedCryptoAmount: "0.00039980",
  paymentAddress: "btc-test-address",
  observedTotal: "0.00039980",
  completedAt: "2026-07-04T12:30:00Z",
  settledAt: null,
  returnUrl: "https://shop.example.test/orders/order-123",
  paymentOptions: [
    { supportedCurrency: "BTC", status: "available", unavailableReasonCode: null }
  ]
};

test("Payer sees completion and chooses the Return URL", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 720 });
  await page.route("**/api/payer/payments/payer-123", (route) =>
    route.fulfill({ json: payment })
  );

  await page.goto("/pay/payer-123");

  await expect(page.getByText("Payment completed")).toBeVisible();
  const returnLink = page.getByRole("link", { name: "Return to shop" });
  await expect(returnLink).toHaveAttribute(
    "href",
    "https://shop.example.test/orders/order-123"
  );
  await expect(page).toHaveURL(/\/pay\/payer-123$/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await returnLink.focus();
  await expect(returnLink).toBeFocused();
  expect(await returnLink.evaluate((element) => getComputedStyle(element).outlineStyle)).not.toBe("none");
  await page.getByRole("combobox", { name: "Color theme" }).selectOption("dark");
  await expect(page.locator("html")).toHaveClass(/dark/);
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  // Existing hosted links remain direct-load and reload safe after the SPA migration.
  await page.reload();
  await expect(page.getByText("Payment completed")).toBeVisible();
});

test("Payer selects a currency, follows the wallet instruction, and sees polling complete", async ({
  page
}) => {
  let selected = false;
  let completed = false;
  await page.route("**/api/payer/payments/payer-polling**", async (route) => {
    if (route.request().method() === "POST") {
      selected = true;
      return route.fulfill({
        json: {
          ...payment,
          status: "waiting_for_payment",
          selectedCurrency: "BTC",
          completedAt: null,
          returnUrl: null
        }
      });
    }
    return route.fulfill({
      json: selected
        ? {
            ...payment,
            status: completed ? "completed" : "waiting_for_payment",
            completedAt: completed ? payment.completedAt : null,
            returnUrl: null
          }
        : {
            ...payment,
            status: "pending_currency_selection",
            selectedCurrency: null,
            expectedCryptoAmount: null,
            paymentAddress: null,
            completedAt: null,
            returnUrl: null
          }
    });
  });

  await page.goto("/pay/payer-polling");
  await page.getByRole("button", { name: "Pay with BTC" }).click();

  await expect(page.getByText("Waiting for payment")).toBeVisible();
  await expect(page.getByText("0.00039980 BTC")).toBeVisible();
  await expect(page.getByText("btc-test-address")).toBeVisible();
  await expect(
    page.getByRole("link", { name: "Open payment instruction in a compatible wallet" })
  ).toHaveAttribute("href", "bitcoin:btc-test-address?amount=0.00039980");
  await expect(page.getByLabel("QR code for the payment instruction")).toBeVisible();

  completed = true;
  await page.waitForTimeout(5100);
  await expect(page.getByText("Payment completed")).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});

test("Payer sees unavailable options and the expired state without horizontal overflow", async ({
  page
}) => {
  let state: "unavailable" | "expired" | "error" = "unavailable";
  await page.setViewportSize({ width: 320, height: 720 });
  await page.route("**/api/payer/payments/payer-unavailable", (route) => {
    if (state === "error") {
      return route.fulfill({
        status: 503,
        json: {
          title: "Provider details that must stay hidden.",
          code: "provider.unavailable",
          correlationId: "payer-correlation-123"
        }
      });
    }
    return route.fulfill({
      json:
        state === "expired"
          ? { ...payment, status: "expired", returnUrl: null }
          : {
            ...payment,
            status: "pending_currency_selection",
            selectedCurrency: null,
            expectedCryptoAmount: null,
            paymentAddress: null,
            completedAt: null,
            returnUrl: null,
            paymentOptions: [
              {
                supportedCurrency: "BTC",
                status: "unavailable",
                unavailableReasonCode: "blockchain_observation.unavailable"
              },
              {
                supportedCurrency: "LTC",
                status: "unavailable",
                unavailableReasonCode: "exchange_rate.unavailable"
              }
            ]
          }
    });
  });

  await page.goto("/pay/payer-unavailable");
  await expect(page.getByText(/No Payment Option is currently usable/)).toBeVisible();
  await expect(page.getByRole("button", { name: "BTC unavailable" })).toBeDisabled();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);

  state = "expired";
  await page.reload();
  await expect(page.getByText("Payment expired")).toBeVisible();
  await expect(page.getByText(/regular payment window has expired/)).toBeVisible();
  await expect(page.getByLabel("QR code for the payment instruction")).toHaveCount(0);
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  state = "error";
  await page.reload();
  await expect(page.getByText("Payment details could not be loaded.")).toBeVisible();
  await expect(page.getByText("Correlation ID: payer-correlation-123")).toBeVisible();
  await expect(page.getByText("Provider details that must stay hidden.")).not.toBeVisible();
});

test("Admin signs in, navigates the admin sections, and writes with CSRF", async ({ page }) => {
  let authenticated = false;
  let csrfHeader: string | undefined;
  const consoleMessages: string[] = [];
  page.on("console", (message) => consoleMessages.push(message.text()));

  await page.route("**/api/admin/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/admin/session") {
      return authenticated
        ? route.fulfill({
            json: {
              status: "authenticated",
              adminAccountId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
              username: "admin@example.test",
              mfaAuthenticatedAt: "2026-07-05T10:00:00Z",
              stepUpAuthenticatedAt: "2026-07-05T10:00:00Z",
              expiresAt: "2026-07-12T10:00:00Z",
              idleExpiresAt: "2026-07-05T22:00:00Z"
            }
          })
        : route.fulfill({
            status: 401,
            json: { title: "Authentication is invalid.", code: "admin_session.invalid" }
          });
    }
    if (path === "/api/admin/auth/login") {
      return route.fulfill({
        json: {
          status: "mfa_required",
          challengeId: "f53cb01d-5913-4af5-80f9-09f55aa85b30"
        }
      });
    }
    if (path === "/api/admin/auth/mfa") {
      authenticated = true;
      return route.fulfill({ json: { status: "authenticated" } });
    }
    if (path === "/api/admin/csrf") {
      return route.fulfill({ json: { csrfToken: "csrf-token" } });
    }
    if (path === "/api/admin/integration-api-credentials" && request.method() === "POST") {
      csrfHeader = request.headers()["x-csrf-token"];
      return route.fulfill({
        status: 201,
        json: {
          credential: {
            id: "8b6c093e-4d15-499e-a156-9d44a1ee835f",
            name: "Playwright integration",
            status: "active",
            createdAt: "2026-07-05T11:00:00Z",
            lastUsedAt: null,
            updatedAt: "2026-07-05T11:00:00Z",
            version: 1
          },
          token: "payaffe_playwright_one_time_token"
        }
      });
    }

    const emptyResponses: Record<string, unknown> = {
      "/api/admin/projects": {
        projects: [
          {
            projectId: "00000000-0000-0000-0000-000000000001",
            name: "Default Project",
            slug: "default",
            status: "active",
            createdAt: "2026-07-05T10:00:00Z",
            updatedAt: "2026-07-05T10:00:00Z",
            version: 1
          },
          {
            projectId: "ebc46c0b-c785-47d5-a2b6-5d417374bd79",
            name: "Second Project",
            slug: "second",
            status: "active",
            createdAt: "2026-07-05T10:00:00Z",
            updatedAt: "2026-07-05T10:00:00Z",
            version: 1
          }
        ]
      },
      "/api/admin/payments": { payments: [] },
      "/api/admin/audit-log": { entries: [] },
      "/api/admin/webhook-deliveries": { deliveries: [] },
      "/api/admin/reorg-alerts": { alerts: [] },
      "/api/admin/integration-api-credentials": { credentials: [] },
      "/api/admin/webhook-endpoints": { endpoints: [] },
      "/api/admin/observation-health": { currencies: [] },
      "/api/admin/native-eth-address-pool": {
        unusedCount: 0,
        assignedCount: 0,
        retiredCount: 0,
        lowCapacityThreshold: 5,
        isLowCapacity: true
      }
    };
    return route.fulfill({ json: emptyResponses[path] ?? {} });
  });

  // An unauthenticated admin is sent to the sign-in route rather than shown
  // panels that each fail on their own.
  await page.goto("/admin");
  await expect(page).toHaveURL(/\/admin\/login$/);

  await page.getByLabel("Username").focus();
  await page.keyboard.type("admin@example.test");
  await page.keyboard.press("Tab");
  await expect(page.getByLabel("Password")).toBeFocused();
  await page.keyboard.type("correct-password");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Continue" })).toBeFocused();
  await page.keyboard.press("Enter");
  await page.getByLabel("Authentication code").focus();
  await page.keyboard.type("123456");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Sign in" })).toBeFocused();
  await page.keyboard.press("Enter");

  // A multi-Project installation requires an explicit route choice.
  await expect(page).toHaveURL(/\/admin$/);
  await expect(
    page.getByRole("heading", { level: 1, name: "Installation overview" })
  ).toBeVisible();
  await page.getByRole("link", { name: /Default Project/ }).click();
  await expect(page).toHaveURL(
    /\/admin\/projects\/00000000-0000-0000-0000-000000000001$/
  );
  await expect(page.getByRole("heading", { level: 1, name: "Overview" })).toBeVisible();
  const navigation = page.getByRole("navigation", { name: "Admin sections" });
  await expect(navigation.getByRole("link", { name: "Overview" })).toHaveAttribute(
    "aria-current",
    "page"
  );
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  await navigation.getByRole("link", { name: "Integrations" }).click();
  await expect(page).toHaveURL(
    /\/admin\/projects\/00000000-0000-0000-0000-000000000001\/integrations$/
  );
  await expect(
    page.getByRole("heading", { level: 1, name: "Integration API Credentials" })
  ).toBeVisible();
  await expect(navigation.getByRole("link", { name: "Integrations" })).toHaveAttribute(
    "aria-current",
    "page"
  );

  await page.getByLabel("Credential name").focus();
  await page.keyboard.type("Playwright integration");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Create credential" })).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(page.getByText("payaffe_playwright_one_time_token")).toBeVisible();
  expect(csrfHeader).toBe("csrf-token");
  const browserStorage = await page.evaluate(() => ({
    local: Object.fromEntries(Object.entries(localStorage)),
    session: Object.fromEntries(Object.entries(sessionStorage))
  }));
  const storedValues = JSON.stringify(browserStorage);
  expect(storedValues).not.toContain("payaffe_playwright_one_time_token");
  expect(storedValues).not.toContain("correct-password");
  expect(storedValues).not.toContain("123456");
  expect(consoleMessages.join("\n")).not.toContain("payaffe_playwright_one_time_token");
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  await page.getByRole("combobox", { name: "Project" }).selectOption(
    "ebc46c0b-c785-47d5-a2b6-5d417374bd79"
  );
  await expect(page).toHaveURL(
    /\/admin\/projects\/ebc46c0b-c785-47d5-a2b6-5d417374bd79\/integrations$/
  );
  await expect(page.getByText("payaffe_playwright_one_time_token")).not.toBeVisible();

  await page.setViewportSize({ width: 320, height: 720 });
  await page.getByRole("button", { name: "Menu" }).click();
  await expect(navigation.getByRole("link", { name: "Payments" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
});

test("Admin confirms settlement, address import, webhook resend, and audit export", async ({
  page
}) => {
  const projectId = "00000000-0000-0000-0000-000000000001";
  const paymentId = "03a26b78-c1f3-4230-99d7-9852cedcc181";
  const webhookEventId = "4c5b4f2a-df57-4804-8a6f-dce55b2e680a";
  const mutationHeaders: string[] = [];
  let addressCount = 8;
  let settled = false;

  await page.route("**/api/admin/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/admin/session") {
      return route.fulfill({
        json: {
          status: "authenticated",
          adminAccountId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
          username: "admin@example.test",
          mfaAuthenticatedAt: "2026-07-05T10:00:00Z",
          stepUpAuthenticatedAt: "2026-07-05T10:00:00Z",
          expiresAt: "2026-07-12T10:00:00Z",
          idleExpiresAt: "2026-07-05T22:00:00Z"
        }
      });
    }
    if (path === "/api/admin/csrf") {
      return route.fulfill({ json: { csrfToken: "csrf-token" } });
    }
    if (request.method() === "POST") {
      mutationHeaders.push(request.headers()["x-csrf-token"] ?? "");
    }
    if (path === `/api/admin/payments/${paymentId}` && request.method() === "GET") {
      return route.fulfill({
        json: {
          paymentId,
          payerPageId: "payer-page-123",
          externalReference: "order-123",
          fiatCurrency: "EUR",
          fiatAmountMinor: 1999,
          status: settled ? "settled" : "observed",
          selectedCurrency: "BTC",
          expectedCryptoAmount: "0.00039980",
          paymentAddress: "btc-test-address",
          expiresAt: "2026-07-05T11:00:00Z",
          lateAcceptanceEndsAt: "2026-07-06T11:00:00Z",
          observedTotal: "0.00039980",
          confirmedEligibleTotal: "0.00039980",
          completedAt: null,
          settledAt: settled ? "2026-07-05T11:00:00Z" : null,
          createdAt: "2026-07-05T10:00:00Z",
          updatedAt: "2026-07-05T10:05:00Z",
          version: settled ? 4 : 3
        }
      });
    }
    if (path === `/api/admin/payments/${paymentId}/settle`) {
      const body = await request.postDataJSON();
      expect(body).toEqual({ projectId, expectedVersion: 3, reason: "Verified with partner" });
      settled = true;
      return route.fulfill({
        json: {
          paymentId,
          payerPageId: "payer-page-123",
          externalReference: "order-123",
          fiatCurrency: "EUR",
          fiatAmountMinor: 1999,
          status: "settled",
          selectedCurrency: "BTC",
          expectedCryptoAmount: "0.00039980",
          paymentAddress: "btc-test-address",
          expiresAt: "2026-07-05T11:00:00Z",
          lateAcceptanceEndsAt: "2026-07-06T11:00:00Z",
          observedTotal: "0.00039980",
          confirmedEligibleTotal: "0.00039980",
          completedAt: null,
          settledAt: "2026-07-05T11:00:00Z",
          createdAt: "2026-07-05T10:00:00Z",
          updatedAt: "2026-07-05T11:00:00Z",
          version: 4
        }
      });
    }
    if (path === "/api/admin/native-eth-address-pool/import") {
      const body = await request.postDataJSON();
      expect(body).toEqual({
        projectId,
        addresses: ["0x1111111111111111111111111111111111111111"]
      });
      addressCount += 1;
      return route.fulfill({
        status: 201,
        json: {
          importId: "596de3ac-7fab-41f8-b2a4-5d40d8e70e7e",
          importedCount: 1,
          summary: {
            unusedCount: addressCount,
            assignedCount: 2,
            retiredCount: 0,
            lowCapacityThreshold: 5,
            isLowCapacity: false
          }
        }
      });
    }
    if (path === `/api/admin/webhook-deliveries/${webhookEventId}/resend`) {
      return route.fulfill({ json: { status: "resent", deliveryStatus: "delivered" } });
    }
    if (path === "/api/admin/audit-log/export") {
      return route.fulfill({
        json: {
          exportedAt: "2026-07-05T10:30:00Z",
          entries: [
            {
              eventId: "02ba688c-2a8d-4a20-b783-41a0c2e6a6fa",
              projectId,
              occurredAt: "2026-07-05T10:15:00Z",
              eventType: "payment.settled",
              outcome: "success",
              actorType: "product_user",
              actorId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
              reasonCode: "payment.settled",
              subjectType: "payment",
              subjectId: paymentId,
              sourceService: "api",
              sourceIp: null,
              userAgent: null,
              correlationId: "trace-123"
            }
          ]
        }
      });
    }

    const responses: Record<string, unknown> = {
      "/api/admin/projects": {
        projects: [
          {
            projectId,
            name: "Default Project",
            slug: "default",
            status: "active",
            createdAt: "2026-07-05T10:00:00Z",
            updatedAt: "2026-07-05T10:00:00Z",
            version: 1
          }
        ]
      },
      "/api/admin/payments": { payments: [] },
      "/api/admin/audit-log": { entries: [] },
      "/api/admin/webhook-deliveries": {
        deliveries: [
          {
            webhookEventId,
            paymentId,
            paymentExternalReference: "order-123",
            eventType: "payment.created",
            eventVersion: "1",
            status: "retry_pending",
            attemptCount: 1,
            lastErrorCode: "http.503",
            nextAttemptAt: "2026-07-05T10:25:00Z",
            occurredAt: "2026-07-05T10:00:00Z",
            createdAt: "2026-07-05T10:00:00Z",
            lastAttemptedAt: "2026-07-05T10:20:00Z",
            lastAttemptResult: "retry_pending",
            lastHttpStatusCode: 503,
            lastSafeErrorCode: "http.503",
            correlationId: "trace-webhook"
          }
        ]
      },
      "/api/admin/reorg-alerts": { alerts: [] },
      "/api/admin/integration-api-credentials": { credentials: [] },
      "/api/admin/webhook-endpoints": { endpoints: [] },
      "/api/admin/observation-health": { currencies: [] },
      "/api/admin/native-eth-address-pool": {
        unusedCount: addressCount,
        assignedCount: 2,
        retiredCount: 0,
        lowCapacityThreshold: 5,
        isLowCapacity: false
      }
    };
    return route.fulfill({ json: responses[path] ?? {} });
  });

  await page.goto(`/admin/projects/${projectId}/payments/${paymentId}`);
  await page.getByLabel("Settlement reason").fill("Verified with partner");
  page.once("dialog", async (dialog) => {
    expect(dialog.message()).toContain("Settle order-123?");
    await dialog.accept();
  });
  await page.getByRole("button", { name: "Settle Payment" }).click();
  await expect(page.getByText("settled", { exact: true })).toBeVisible();

  await page.goto(`/admin/projects/${projectId}/addresses`);
  await page.getByLabel("Payment Addresses").fill("0x1111111111111111111111111111111111111111");
  await page.getByRole("button", { name: "Import addresses" }).click();
  await expect(page.getByText("9", { exact: true })).toBeVisible();

  await page.goto(`/admin/projects/${projectId}/webhooks?view=deliveries`);
  page.once("dialog", async (dialog) => {
    expect(dialog.message()).toContain("Resend payment.created for order-123?");
    await dialog.accept();
  });
  await page.getByRole("button", { name: "Resend" }).click();
  await expect(page.getByText("Resend completed with status delivered.")).toBeVisible();

  await page.goto("/admin/audit-log");
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export JSON" }).click();
  await download;
  await expect(page.getByText(/Exported 1 event/)).toBeVisible();

  expect(mutationHeaders).toEqual(["csrf-token", "csrf-token", "csrf-token", "csrf-token"]);
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});

test("Admin reaches the Webhook Deliveries view by its own URL", async ({ page }) => {
  await page.route("**/api/admin/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/admin/session") {
      return route.fulfill({
        json: {
          status: "authenticated",
          adminAccountId: "f30c6d61-8ce9-488b-93bb-33f72dbf6ac1",
          username: "admin@example.test",
          mfaAuthenticatedAt: "2026-07-05T10:00:00Z",
          stepUpAuthenticatedAt: "2026-07-05T10:00:00Z",
          expiresAt: "2026-07-12T10:00:00Z",
          idleExpiresAt: "2026-07-05T22:00:00Z"
        }
      });
    }
    if (path === "/api/admin/webhook-deliveries") {
      return route.fulfill({
        json: {
          deliveries: [
            {
              webhookEventId: "4c5b4f2a-df57-4804-8a6f-dce55b2e680a",
              paymentId: "78d8a09b-1c4a-4b6e-9f94-f8acbd4278f1",
              paymentExternalReference: "order-123",
              eventType: "payment.created",
              eventVersion: "1",
              status: "retry_pending",
              attemptCount: 1,
              lastErrorCode: "http.503",
              nextAttemptAt: "2026-07-05T10:25:00Z",
              occurredAt: "2026-07-05T10:00:00Z",
              createdAt: "2026-07-05T10:00:00Z",
              lastAttemptedAt: "2026-07-05T10:20:00Z",
              lastAttemptResult: "retry_pending",
              lastHttpStatusCode: 503,
              lastSafeErrorCode: "http.503",
              correlationId: "trace-webhook"
            }
          ]
        }
      });
    }

    const emptyResponses: Record<string, unknown> = {
      "/api/admin/projects": {
        projects: [
          {
            projectId: "00000000-0000-0000-0000-000000000001",
            name: "Default Project",
            slug: "default",
            status: "active",
            createdAt: "2026-07-05T10:00:00Z",
            updatedAt: "2026-07-05T10:00:00Z",
            version: 1
          }
        ]
      },
      "/api/admin/payments": { payments: [] },
      "/api/admin/audit-log": { entries: [] },
      "/api/admin/reorg-alerts": { alerts: [] },
      "/api/admin/integration-api-credentials": { credentials: [] },
      "/api/admin/webhook-endpoints": { endpoints: [] },
      "/api/admin/observation-health": { currencies: [] },
      "/api/admin/native-eth-address-pool": {
        unusedCount: 0,
        assignedCount: 0,
        retiredCount: 0,
        lowCapacityThreshold: 5,
        isLowCapacity: false
      }
    };
    return route.fulfill({ json: emptyResponses[path] ?? {} });
  });

  await page.goto("/admin/webhooks?view=deliveries");

  await expect(page.getByRole("link", { name: "Deliveries" })).toHaveAttribute(
    "aria-current",
    "page"
  );
  await expect(page.getByRole("heading", { level: 2, name: "Webhook deliveries" })).toBeVisible();
  await expect(page.getByText("http.503")).toBeVisible();
  // The count that the navigation carries comes from the same list.
  await expect(
    page.getByRole("navigation", { name: "Admin sections" }).getByRole("link", {
      name: "Webhooks, 1 needs attention"
    })
  ).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});

test("Payer of a test mode payment simulates paying and sees it complete", async ({ page }) => {
  const testPayment = {
    ...payment,
    status: "waiting_for_payment",
    completedAt: null,
    observedTotal: null,
    returnUrl: null,
    payerPageUrl: "https://pay.example.test/pay/payer-test-mode",
    paymentAddress: "tb1qsimulated",
    testMode: true,
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
  let simulatedAmount: unknown = "not requested";
  await page.route("**/api/payer/payments/payer-test-mode**", async (route) => {
    if (route.request().method() === "POST") {
      simulatedAmount = route.request().postDataJSON().amount;
      return route.fulfill({
        status: 201,
        json: {
          paymentId: testPayment.paymentId,
          supportedCurrency: "BTC",
          paymentAddress: "tb1qsimulated",
          transactionHash: "a".repeat(64),
          amount: "0.0003998",
          recordedAt: "2026-07-04T12:10:00Z"
        }
      });
    }

    return route.fulfill({
      json: simulatedAmount === "not requested"
        ? testPayment
        : { ...testPayment, status: "completed", completedAt: "2026-07-04T12:30:00Z" }
    });
  });

  await page.goto("/pay/payer-test-mode");

  await expect(page.getByRole("region", { name: "Test mode" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Open payment instruction in a compatible wallet" })).toHaveAttribute(
    "href",
    "bitcoin:?tb=tb1qsimulated&amount=0.0003998"
  );
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  await page.getByRole("button", { name: "I have paid" }).click();

  // The confirmation is shown only while the Payment still waits: the page
  // refetches at once, and a completed Payment has nothing left to simulate.
  await expect(page.getByText("Payment completed")).toBeVisible();
  expect(simulatedAmount).toBeNull();
  await expect(page.getByRole("button", { name: "I have paid" })).toHaveCount(0);
});
