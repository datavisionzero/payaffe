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
});

test("Admin signs in, navigates the admin sections, and writes with CSRF", async ({ page }) => {
  let authenticated = false;
  let csrfHeader: string | undefined;

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
