import { Component, lazy, Suspense, type ErrorInfo, type ReactNode } from "react";
import { Navigate, Outlet, Route, Routes, useParams } from "react-router";
import { AdminLoginPage } from "./components/admin/login-page";
import { AdminShell } from "./components/admin/admin-shell";
import { StepUpProvider } from "./components/admin/step-up";
import Link from "./lib/link";
import { reportClientError } from "./lib/client-errors";

const AdminAccountPage = lazy(() =>
  import("./components/admin/account-page").then((module) => ({ default: module.AdminAccountPage }))
);
const AdminAddressesPage = lazy(() =>
  import("./components/admin/addresses-page").then((module) => ({ default: module.AdminAddressesPage }))
);
const AdminAuditLogDetailPage = lazy(() =>
  import("./components/admin/audit-log-detail-page").then((module) => ({
    default: module.AdminAuditLogDetailPage
  }))
);
const AdminAuditLogPage = lazy(() =>
  import("./components/admin/audit-log-page").then((module) => ({ default: module.AdminAuditLogPage }))
);
const AdminIntegrationsPage = lazy(() =>
  import("./components/admin/integrations-page").then((module) => ({
    default: module.AdminIntegrationsPage
  }))
);
const AdminMonitoringPage = lazy(() =>
  import("./components/admin/monitoring-page").then((module) => ({ default: module.AdminMonitoringPage }))
);
const AdminOverviewPage = lazy(() =>
  import("./components/admin/overview-page").then((module) => ({ default: module.AdminOverviewPage }))
);
const AdminPaymentDetailPage = lazy(() =>
  import("./components/admin/payment-detail-page").then((module) => ({
    default: module.AdminPaymentDetailPage
  }))
);
const AdminPaymentsPage = lazy(() =>
  import("./components/admin/payments-page").then((module) => ({ default: module.AdminPaymentsPage }))
);
const AdminWebhooksPage = lazy(() =>
  import("./components/admin/webhooks-page").then((module) => ({ default: module.AdminWebhooksPage }))
);
const PayerPage = lazy(() =>
  import("./components/payer-page").then((module) => ({ default: module.PayerPage }))
);
const AdminLandingPage = lazy(() =>
  import("./components/admin/projects-page").then((module) => ({ default: module.AdminLandingPage }))
);
const AdminProjectsPage = lazy(() =>
  import("./components/admin/projects-page").then((module) => ({ default: module.AdminProjectsPage }))
);
const LegacyProjectRedirect = lazy(() =>
  import("./components/admin/projects-page").then((module) => ({
    default: module.LegacyProjectRedirect
  }))
);

export function App() {
  return (
    <AppErrorBoundary>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/pay/:payerPageId" element={<PayerRoute />} />
        <Route path="/admin/login" element={<AdminLoginPage />} />
        <Route path="/admin" element={<AdminShellRoute />}>
          <Route index element={<AdminLandingPage />} />
          <Route path="projects" element={<AdminProjectsPage />} />
          <Route path="projects/:projectId">
            <Route index element={<AdminOverviewPage />} />
            <Route path="payments" element={<AdminPaymentsPage />} />
            <Route path="payments/:paymentId" element={<AdminPaymentRoute />} />
            <Route path="monitoring" element={<AdminMonitoringPage />} />
            <Route path="webhooks" element={<AdminWebhooksPage />} />
            <Route path="integrations" element={<AdminIntegrationsPage />} />
            <Route path="addresses" element={<AdminAddressesPage />} />
          </Route>
          <Route path="payments" element={<LegacyProjectRedirect suffix="/payments" />} />
          <Route path="payments/:paymentId" element={<LegacyPaymentRoute />} />
          <Route path="monitoring" element={<LegacyProjectRedirect suffix="/monitoring" />} />
          <Route path="webhooks" element={<LegacyProjectRedirect suffix="/webhooks" />} />
          <Route path="integrations" element={<LegacyProjectRedirect suffix="/integrations" />} />
          <Route path="addresses" element={<LegacyProjectRedirect suffix="/addresses" />} />
          <Route path="audit-log" element={<AdminAuditLogPage />} />
          <Route path="audit-log/:eventId" element={<AdminAuditLogRoute />} />
          <Route path="account" element={<AdminAccountPage />} />
          <Route path="*" element={<AdminNotFoundPage />} />
        </Route>
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </AppErrorBoundary>
  );
}

function PayerRouteLoading() {
  return (
    <main aria-live="polite" className="mx-auto min-h-screen w-full max-w-3xl px-6 py-12">
      <h1 className="text-xl font-semibold">Loading payment</h1>
    </main>
  );
}

function HomePage() {
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col justify-center px-6 py-12">
      <h1 className="text-3xl font-semibold">payaffe</h1>
      <p className="mt-3 max-w-xl text-base text-[var(--muted-foreground)]">
        Payment pages are available through payment-specific links.
      </p>
      <Link className="mt-8 text-sm font-medium text-[var(--brand-ink)]" href="/admin">
        Admin sign-in
      </Link>
    </main>
  );
}

function AdminShellRoute() {
  return (
    <AdminShell>
      <StepUpProvider>
        <Suspense fallback={<AdminRouteLoading />}>
          <Outlet />
        </Suspense>
      </StepUpProvider>
    </AdminShell>
  );
}

function AdminRouteLoading() {
  return (
    <div aria-live="polite" className="rounded-md border border-[var(--border)] p-5 text-sm">
      <h1 className="text-xl font-semibold">Loading Admin view</h1>
    </div>
  );
}

function PayerRoute() {
  const { payerPageId } = useParams();
  return payerPageId ? (
    <Suspense fallback={<PayerRouteLoading />}>
      <PayerPage payerPageId={payerPageId} />
    </Suspense>
  ) : (
    <Navigate replace to="/" />
  );
}

function AdminPaymentRoute() {
  const { paymentId } = useParams();
  return paymentId ? <AdminPaymentDetailPage paymentId={paymentId} /> : <Navigate replace to="/admin" />;
}

function LegacyPaymentRoute() {
  const { paymentId } = useParams();
  return <LegacyProjectRedirect suffix={paymentId ? `/payments/${paymentId}` : "/payments"} />;
}

function AdminAuditLogRoute() {
  const { eventId } = useParams();
  return eventId ? <AdminAuditLogDetailPage eventId={eventId} /> : <Navigate replace to="/admin/audit-log" />;
}

function NotFoundPage() {
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-xl flex-col justify-center px-6 py-12">
      <h1 className="text-2xl font-semibold">Page not found</h1>
      <p className="mt-3 text-sm text-[var(--muted-foreground)]">
        The address does not identify a payaffe screen.
      </p>
      <Link className="mt-5 text-sm font-medium text-[var(--brand-ink)]" href="/">
        Return home
      </Link>
    </main>
  );
}

function AdminNotFoundPage() {
  return (
    <div className="mx-auto max-w-xl py-8">
      <h1 className="text-2xl font-semibold">Admin page not found</h1>
      <p className="mt-3 text-sm text-[var(--muted-foreground)]">
        The address does not identify an Admin view.
      </p>
      <Link className="mt-5 inline-block text-sm font-medium text-[var(--brand-ink)]" href="/admin/projects">
        View Projects
      </Link>
    </div>
  );
}

class AppErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, _errorInfo: ErrorInfo) {
    reportClientError(error);
  }

  render() {
    if (!this.state.error) {
      return this.props.children;
    }

    return (
      <main className="mx-auto max-w-xl px-5 py-12">
        <h1 className="text-2xl font-semibold">The page could not be displayed.</h1>
        <p className="mt-3 text-sm">
          Retry the page. If the error continues, contact the operator.
        </p>
        <button
          className="mt-5 rounded-md bg-[var(--brand)] px-4 py-2 font-semibold text-[var(--brand-foreground)]"
          onClick={() => this.setState({ error: null })}
          type="button"
        >
          Retry
        </button>
      </main>
    );
  }
}
