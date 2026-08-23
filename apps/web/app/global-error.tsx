"use client";

import { reportClientError } from "@/lib/client-errors";
import { useEffect } from "react";

export default function GlobalError({
  error,
  reset
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  // React routes a render error here rather than to `window.onerror`, so this
  // is the only path that catches one.
  useEffect(() => {
    reportClientError(error);
  }, [error]);

  return (
    <html lang="en">
      <body>
        <main className="mx-auto max-w-xl px-5 py-12">
          <h1 className="text-2xl font-semibold">The page could not be displayed.</h1>
          <p className="mt-3 text-sm">Retry the page. If the error continues, contact the operator.</p>
          <button
            className="mt-5 rounded-md bg-[var(--accent)] px-4 py-2 font-semibold text-white"
            onClick={reset}
            type="button"
          >
            Retry
          </button>
        </main>
      </body>
    </html>
  );
}
