import { afterEach, describe, expect, it, vi } from "vitest";
import { reportClientError } from "../lib/client-errors";

afterEach(() => {
  vi.restoreAllMocks();
  window.history.replaceState(null, "", "/");
});

describe("reportClientError", () => {
  it("keeps the Payer Page ID out of the reported path", () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 202 }));
    window.history.replaceState(null, "", "/pay/secret-payer-page-id?locale=en");

    reportClientError(new Error("render failed"));

    expect(fetch).toHaveBeenCalledTimes(1);
    const body = JSON.parse(String(fetch.mock.calls[0][1]?.body)) as { path: string };
    expect(body.path).toBe("/pay/{payerPageId}");
    expect(JSON.stringify(fetch.mock.calls[0])).not.toContain("secret-payer-page-id");
  });

  it("reports other paths unchanged", () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 202 }));
    window.history.replaceState(null, "", "/admin/projects/p1/payments");

    reportClientError(new Error("render failed"));

    const body = JSON.parse(String(fetch.mock.calls[0][1]?.body)) as { path: string };
    expect(body.path).toBe("/admin/projects/p1/payments");
  });
});
