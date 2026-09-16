import { Component, type ErrorInfo, type ReactNode } from "react";
import { Navigate, Outlet, Route, Routes, useParams } from "react-router";
import { AdminAccountPage } from "./components/admin/account-page";
import { AdminAddressesPage } from "./components/admin/addresses-page";
import { AdminAuditLogDetailPage } from "./components/admin/audit-log-detail-page";
import { AdminAuditLogPage } from "./components/admin/audit-log-page";
import { AdminIntegrationsPage } from "./components/admin/integrations-page";
import { AdminLoginPage } from "./components/admin/login-page";
import { AdminMonitoringPage } from "./components/admin/monitoring-page";
import { AdminOverviewPage } from "./components/admin/overview-page";
import { AdminPaymentDetailPage } from "./components/admin/payment-detail-page";
import { AdminPaymentsPage } from "./components/admin/payments-page";
import { AdminShell } from "./components/admin/admin-shell";
import { AdminWebhooksPage } from "./components/admin/webhooks-page";
import { PayerPage } from "./components/payer-page";
import Link from "./lib/link";
import { reportClientError } from "./lib/client-errors";

export function App() {
  return (
    <AppErrorBoundary>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/pay/:payerPageId" element={<PayerRoute />} />
        <Route path="/admin/login" element={<AdminLoginPage />} />
        <Route path="/admin" element={<AdminShellRoute />}>
          <Route index element={<AdminOverviewPage />} />
          <Route path="payments" element={<AdminPaymentsPage />} />
          <Route path="payments/:paymentId" element={<AdminPaymentRoute />} />
          <Route path="monitoring" element={<AdminMonitoringPage />} />
          <Route path="webhooks" element={<AdminWebhooksPage />} />
          <Route path="integrations" element={<AdminIntegrationsPage />} />
          <Route path="addresses" element={<AdminAddressesPage />} />
          <Route path="audit-log" element={<AdminAuditLogPage />} />
          <Route path="audit-log/:eventId" element={<AdminAuditLogRoute />} />
          <Route path="account" element={<AdminAccountPage />} />
        </Route>
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </AppErrorBoundary>
  );
}

function HomePage() {
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col justify-center px-6 py-12">
      <h1 className="text-3xl font-semibold">payaffe</h1>
      <p className="mt-3 max-w-xl text-base text-[var(--muted-foreground)]">
        Payment pages are available through payment-specific links.
      </p>
      <Link className="mt-8 text-sm font-medium text-[var(--accent)]" href="/admin">
        Admin sign-in
      </Link>
    </main>
  );
}

function AdminShellRoute() {
  return (
    <AdminShell>
      <Outlet />
    </AdminShell>
  );
}

function PayerRoute() {
  const { payerPageId } = useParams();
  return payerPageId ? <PayerPage payerPageId={payerPageId} /> : <Navigate replace to="/" />;
}

function AdminPaymentRoute() {
  const { paymentId } = useParams();
  return paymentId ? <AdminPaymentDetailPage paymentId={paymentId} /> : <Navigate replace to="/admin" />;
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
      <Link className="mt-5 text-sm font-medium text-[var(--accent)]" href="/">
        Return home
      </Link>
    </main>
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
          className="mt-5 rounded-md bg-[var(--accent)] px-4 py-2 font-semibold text-white"
          onClick={() => this.setState({ error: null })}
          type="button"
        >
          Retry
        </button>
      </main>
    );
  }
}
