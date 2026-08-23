# An Error Is an Entry, and There Is No Error Tracker

GlitchTip is gone. Errors are log entries, they go where every other entry goes
([ADR 0025](./0025-logs-are-delivered-to-logaffe.md)), and payaffe no longer
sends anything to a second service. The Sentry SDK is out of the backend, the
browser bundle carries no DSN, and there is one fewer secret to rotate and one
fewer thing for an operator to run.

This supersedes the part of ADR 0025 that kept GlitchTip. That decision was
right when it was made and its reasoning was correct: logaffe could store an
error but could not tell anyone about one, and removing the tracker would have
left nobody told. What changed is logaffe, which now has a fourth alert
condition — a project's entries at `Error` or above above ten times the median
of that hour of the day, above a floor of ten, and true of two closed hours in a
row. That is the capability the tracker was still being run for.

What is given up is real and worth naming. There is no grouping: a hundred
occurrences of one exception are a hundred entries, not one issue with a count.
There is no issue state, so nothing is resolved, ignored, or seen to regress.
There is no release dimension, so "introduced in 0.2.0" is not a question that
can be asked. For an installation taking fifteen payments a day those are
luxuries; for one taking fifteen thousand they would not be, and that
installation is not this product ([ADR 0017](./0017-one-installation-serves-one-shop.md)).

Two things that looked like losses turned out not to be. Breadcrumbs are
replaced by something better: `TraceId` and `SpanId` are promoted to indexed
fields, so the entries of the request that failed are one filter away — the real
ones, not twenty reconstructed crumbs. And browser stack traces were never
resolved, because this repository uploads no source maps; what GlitchTip
received was already minified frames.

## Consequences

**A browser error does not raise the error rate, so it does not alert.**
Reports arrive at `POST /api/client-errors`, which is unauthenticated because
the payer page is reachable by anyone, and they are logged at `Warning` for that
reason: an error rate is what the fourth condition is derived from, and logging
an unauthenticated report at `Error` would hand a stranger the operator's phone.
Browser errors are therefore visible and searchable but silent. This is the one
place where removing GlitchTip is a straight loss, and closing it needs an
authenticated reporting path for the Admin UI rather than a change here.

**`Warning` and above is where an operator looks.** The console log of each host
is still complete and is still the fallback. In logaffe, the level filter and
the `SourceContext` are what separate a backend failure from a browser one.

**Nothing carries a release any more.** `PAYAFFE_RELEASE` still names the
`service.version` resource attribute on traces and metrics, but no log entry
carries it, because logaffe has no release dimension to put it in. Correlating
an entry with a version is done through the time it happened and the deployment
that was running.

**The web image lost its last build argument that was not an address.**
`NEXT_PUBLIC_GLITCHTIP_DSN`, `NEXT_PUBLIC_RELEASE`, and
`NEXT_PUBLIC_DEPLOYMENT_ENVIRONMENT` existed for the browser SDK and are gone
with it. The published bundle now carries nothing installation-specific at all,
which is what [ADR 0024](./0024-the-browser-api-lives-under-api-on-one-origin.md)
was aiming at.

**Two dependencies fewer.** `Sentry.Extensions.Logging` and `@sentry/nextjs`,
and with the second one a build step that wanted its own CLI.
