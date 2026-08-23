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
  await page.route("**/payer/payments/payer-123", (route) =>
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
  await returnLink.focus();
  await expect(returnLink).toBeFocused();
  expect(await returnLink.evaluate((element) => getComputedStyle(element).outlineStyle)).not.toBe("none");
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});

test("Admin signs in and performs a CSRF-protected credential write", async ({ page }) => {
  let authenticated = false;
  let csrfHeader: string | undefined;

  await page.route("**/admin/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/admin/session") {
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
    if (path === "/admin/auth/login") {
      return route.fulfill({
        json: {
          status: "mfa_required",
          challengeId: "f53cb01d-5913-4af5-80f9-09f55aa85b30"
        }
      });
    }
    if (path === "/admin/auth/mfa") {
      authenticated = true;
      return route.fulfill({ json: { status: "authenticated" } });
    }
    if (path === "/admin/csrf") {
      return route.fulfill({ json: { csrfToken: "csrf-token" } });
    }
    if (path === "/admin/integration-api-credentials" && request.method() === "POST") {
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
      "/admin/payments": { payments: [] },
      "/admin/audit-log": { entries: [] },
      "/admin/webhook-deliveries": { deliveries: [] },
      "/admin/reorg-alerts": { alerts: [] },
      "/admin/integration-api-credentials": { credentials: [] },
      "/admin/webhook-endpoints": { endpoints: [] },
      "/admin/observation-health": { currencies: [] },
      "/admin/native-eth-address-pool": {
        unusedCount: 0,
        assignedCount: 0,
        retiredCount: 0,
        lowCapacityThreshold: 5,
        isLowCapacity: true
      }
    };
    return route.fulfill({ json: emptyResponses[path] ?? {} });
  });

  await page.goto("/admin");
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

  await expect(page.getByRole("heading", { name: "Current session" })).toBeVisible();
  await page.getByLabel("Credential name").focus();
  await page.keyboard.type("Playwright integration");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Create credential" })).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(page.getByText("payaffe_playwright_one_time_token")).toBeVisible();
  expect(csrfHeader).toBe("csrf-token");
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});
