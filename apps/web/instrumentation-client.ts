import * as Sentry from "@sentry/nextjs";
import { installClientErrorReporting } from "./lib/client-errors";

const dsn = process.env.NEXT_PUBLIC_GLITCHTIP_DSN;

Sentry.init({
  dsn,
  enabled: Boolean(dsn),
  environment: process.env.NEXT_PUBLIC_DEPLOYMENT_ENVIRONMENT,
  release: process.env.NEXT_PUBLIC_RELEASE,
  sendDefaultPii: false,
  tracesSampleRate: 0,
  beforeSend(event) {
    if (event.request) {
      delete event.request.cookies;
      delete event.request.data;
      delete event.request.headers;
      if (event.request.url) {
        event.request.url = event.request.url.split("?")[0];
      }
    }
    delete event.user;
    event.breadcrumbs = event.breadcrumbs?.map((breadcrumb) => ({
      category: breadcrumb.category,
      level: breadcrumb.level,
      message: breadcrumb.message,
      timestamp: breadcrumb.timestamp,
      type: breadcrumb.type
    }));
    return event;
  }
});

// Errors the browser could not handle also go to the installation's own API,
// which logs them where the backend logs (ADR 0025). It runs alongside the
// GlitchTip reporting above rather than instead of it, because the alerting
// that would replace GlitchTip does not exist yet.
installClientErrorReporting();

export const onRouterTransitionStart = Sentry.captureRouterTransitionStart;
