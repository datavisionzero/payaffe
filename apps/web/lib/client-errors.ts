import { resolveApiBaseUrl } from "./api/client";

// What a browser may report, and the caps it is cut to here as well as on the
// server. Cutting twice is not redundant: the server cannot trust this, and
// this saves the round trip for a stack trace nobody would keep anyway.
const MAX_NAME = 200;
const MAX_MESSAGE = 1000;
const MAX_STACK = 8000;

// One page load reports a handful of times and then stops. A component that
// throws on every render would otherwise report on every render, and the first
// few are the only ones that say anything new.
const MAX_REPORTS_PER_PAGE = 5;
let reported = 0;

function describe(value: unknown): { name: string; message: string; stack?: string } {
  if (value instanceof Error) {
    return {
      name: value.name.slice(0, MAX_NAME),
      message: value.message.slice(0, MAX_MESSAGE),
      stack: value.stack?.slice(0, MAX_STACK)
    };
  }

  // A rejected promise carries whatever it was rejected with, which is
  // frequently not an Error and occasionally not a string.
  let message: string;
  try {
    message = typeof value === "string" ? value : JSON.stringify(value) ?? String(value);
  } catch {
    message = "(unserialisable rejection value)";
  }

  return { name: "UnhandledRejection", message: message.slice(0, MAX_MESSAGE) };
}

export function reportClientError(value: unknown): void {
  if (typeof window === "undefined" || reported >= MAX_REPORTS_PER_PAGE) {
    return;
  }

  reported += 1;
  const described = describe(value);

  // keepalive so that an error thrown during navigation still leaves; the
  // request outlives the page it was sent from.
  void fetch(`${resolveApiBaseUrl()}/api/client-errors`, {
    method: "POST",
    keepalive: true,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name: described.name,
      message: described.message,
      stack: described.stack,
      // The path only. The server drops a query string too, and neither of us
      // wants what the payer page carries in one.
      path: window.location.pathname
    })
    // Deliberately swallowed. A reporter that reports its own failure is a
    // loop, and the console already has the original error.
  }).catch(() => undefined);
}

export function installClientErrorReporting(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.addEventListener("error", (event) => reportClientError(event.error ?? event.message));
  window.addEventListener("unhandledrejection", (event) => reportClientError(event.reason));
}
