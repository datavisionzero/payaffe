import { installClientErrorReporting } from "./lib/client-errors";

// Errors the browser could not handle are posted to this installation's own API
// and land in its log with everything the backend writes (ADR 0026). There is
// no second reporting service and no DSN in this bundle.
installClientErrorReporting();
