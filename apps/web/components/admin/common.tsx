"use client";

import { createText } from "../../lib/text";
import type React from "react";
import { useId } from "react";
import type { useForm } from "react-hook-form";
import Link from "../../lib/link";
import { cn } from "../../lib/utils";
import { Button, buttonVariants } from "../ui/button";
import { AdminApiError, type CredentialForm } from "../../lib/admin-api";

const t = createText({
  clearSensitiveValue: "Clear sensitive value",
  correlationId: "Correlation ID: {correlationId}",
  oneTimeValue: "Copy this value now. It is only retained in this page until you clear or leave it.",
  "errors.unexpected": "The request could not be completed.",
  "errors.admin_login.invalid": "The credentials are invalid.",
  "errors.admin_mfa.invalid": "The authentication code is invalid.",
  "errors.admin_session.invalid": "The session is no longer valid.",
  "errors.admin_csrf.invalid": "The request expired. Retry the action.",
  "errors.admin_step_up.invalid": "The step-up code is invalid.",
  "errors.admin_step_up.required": "This action needs a recent step-up with your authentication code.",
  "errors.audit_log.not_found": "The Audit Log entry was not found.",
  "errors.webhook_delivery.not_found": "The Webhook Delivery was not found.",
  "errors.webhook_delivery.not_resendable": "The Webhook Delivery cannot be resent.",
  "errors.webhook_delivery.in_progress": "The Webhook Delivery is being delivered right now. Refresh and retry.",
  "errors.project.slug_conflict": "That Project slug is already in use.",
  "errors.project.status_transition_invalid": "That Project status change is not allowed.",
  "errors.project.has_active_work": "The Project still has active payment or delivery work and cannot be archived.",
  "errors.project.not_found": "The Project is no longer available.",
  "errors.rateLimited": "Too many requests. Wait before retrying.",
  "errors.forbidden": "You are not authorized to perform this action.",
  "errors.concurrency": "The resource changed. Refresh and retry.",
  "errors.validationFailed": "Review the highlighted values and retry."
});

export function PageHeader({
  actions,
  description,
  title
}: {
  actions?: React.ReactNode;
  description: string;
  title: string;
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-3 border-b border-[var(--border)] pb-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-1 text-sm text-[var(--muted-foreground)]">{description}</p>
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-3">{actions}</div> : null}
    </div>
  );
}

export function AdminSection({
  actions,
  children,
  description,
  title
}: {
  actions?: React.ReactNode;
  children: React.ReactNode;
  description?: string;
  title: string;
}) {
  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--card)] p-4 sm:p-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">{title}</h2>
          {description ? (
            <p className="mt-1 text-sm text-[var(--muted-foreground)]">{description}</p>
          ) : null}
        </div>
        {actions ? <div className="flex flex-wrap items-center gap-3">{actions}</div> : null}
      </div>
      {children}
    </section>
  );
}

export function Panel({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--card)] p-4 sm:p-5">{children}</div>
  );
}

export function StatusPill({ status }: { status: string }) {
  return (
    <span
      className="inline-flex rounded-md px-2 py-1 text-xs font-medium"
      data-status={status.toLowerCase()}
    >
      {status}
    </span>
  );
}

export function ActionButton({
  busy,
  children,
  onClick
}: {
  busy: boolean;
  children: React.ReactNode;
  onClick: () => void;
}) {
  return (
    <Button disabled={busy} onClick={onClick} size="lg" type="button" variant="outline">
      {children}
    </Button>
  );
}

export function SubmitButton({ busy, children }: { busy: boolean; children: React.ReactNode }) {
  return (
    <Button className="h-10 px-4" disabled={busy} type="submit">
      {children}
    </Button>
  );
}

// Opens a separate view, such as a create page, with the look of a primary
// button; it navigates rather than submits.
export function LinkButton({ children, href }: { children: React.ReactNode; href: string }) {
  return (
    <Link className={cn(buttonVariants(), "h-10 px-4")} href={href}>
      {children}
    </Link>
  );
}

export function CancelLink({ children, href }: { children: React.ReactNode; href: string }) {
  return (
    <Link
      className="inline-flex h-10 items-center rounded-md border border-[var(--border)] px-4 text-sm font-medium hover:bg-[var(--muted)]"
      href={href}
    >
      {children}
    </Link>
  );
}

export function SecondaryButton({
  busy,
  children,
  onClick
}: {
  busy?: boolean;
  children: React.ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      className="rounded-md border border-[var(--border)] bg-[var(--background)] px-3 py-2 text-sm font-medium hover:bg-[var(--muted)] disabled:cursor-wait disabled:opacity-70"
      disabled={busy}
      onClick={onClick}
      type="button"
    >
      {children}
    </button>
  );
}

export function TextField({
  error,
  label,
  ...props
}: React.InputHTMLAttributes<HTMLInputElement> & {
  error?: string;
  label: string;
}) {
  const generatedId = useId();
  const id = props.id ?? `${props.name ?? "field"}-${generatedId}`;
  const errorId = `${id}-error`;
  return (
    <label className="block text-sm font-medium" htmlFor={id}>
      <span>{label}</span>
      <input
        className="mt-2 block h-10 w-full rounded-md border border-[var(--input)] bg-[var(--background)] px-3 text-base outline-none focus:border-[var(--brand)]"
        aria-describedby={error ? errorId : undefined}
        aria-invalid={error ? "true" : undefined}
        id={id}
        {...props}
      />
      {error ? (
        <span className="mt-2 block text-sm text-[var(--danger)]" id={errorId}>
          {error}
        </span>
      ) : null}
    </label>
  );
}

export function InfoItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-sm text-[var(--muted-foreground)]">{label}</dt>
      <dd className="mt-1 break-words text-base font-medium">{value}</dd>
    </div>
  );
}

export function StateMessage({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--card)] p-5 text-sm">
      {children}
    </div>
  );
}

export function EmptyMessage({ children }: { children: React.ReactNode }) {
  return (
    <p className="mt-5 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
      {children}
    </p>
  );
}

export function ErrorMessage({ error }: { error: Error }) {
  if (error instanceof AdminApiError) {
    const message = getAdminErrorMessage(error.code, error.status);
    return (
      <div
        aria-live="assertive"
        className="mt-4 rounded-md border border-[var(--destructive)] bg-[var(--status-danger-bg)] p-4 text-sm text-[var(--status-danger-fg)]"
        role="alert"
      >
        <span>{message}</span>
        {error.correlationId ? (
          <span className="mt-2 block text-[var(--muted-foreground)]">
            {t("correlationId", { correlationId: error.correlationId })}
          </span>
        ) : null}
      </div>
    );
  }

  return <StateMessage>{t("errors.unexpected")}</StateMessage>;
}

export function SensitiveValuePanel({
  label,
  onClear,
  value
}: {
  label: string;
  onClear: () => void;
  value: string;
}) {
  return (
    <div className="mt-5 rounded-md border border-[var(--destructive)] bg-[var(--card)] p-4" role="status">
      <p className="font-semibold">{label}</p>
      <p className="mt-2 break-all font-mono text-sm">{value}</p>
      <p className="mt-2 text-sm text-[var(--muted-foreground)]">{t("oneTimeValue")}</p>
      <button className="mt-3 rounded-md border px-3 py-2 text-sm" onClick={onClear} type="button">
        {t("clearSensitiveValue")}
      </button>
    </div>
  );
}

export function mapFieldError(
  error: Error,
  field: string,
  form: ReturnType<typeof useForm<CredentialForm>>,
  message: string
) {
  if (error instanceof AdminApiError && error.fieldErrors[field]) {
    form.setError("name", { message });
  }
}

export function getAdminErrorMessage(
  code: string | undefined,
  status: number
): string {
  switch (code) {
    case "admin_login.invalid":
      return t("errors.admin_login.invalid");
    case "admin_mfa.invalid":
      return t("errors.admin_mfa.invalid");
    case "admin_session.invalid":
      return t("errors.admin_session.invalid");
    case "admin_csrf.invalid":
      return t("errors.admin_csrf.invalid");
    case "admin_step_up.invalid":
      return t("errors.admin_step_up.invalid");
    case "admin_step_up.required":
      return t("errors.admin_step_up.required");
    case "audit_log.not_found":
      return t("errors.audit_log.not_found");
    case "webhook_delivery.not_found":
      return t("errors.webhook_delivery.not_found");
    case "webhook_delivery.not_resendable":
      return t("errors.webhook_delivery.not_resendable");
    case "webhook_delivery.in_progress":
      return t("errors.webhook_delivery.in_progress");
    case "project.slug_conflict":
      return t("errors.project.slug_conflict");
    case "project.status_transition_invalid":
      return t("errors.project.status_transition_invalid");
    case "project.has_active_work":
      return t("errors.project.has_active_work");
    case "project.not_found":
      return t("errors.project.not_found");
    case "admin_rate_limit.exceeded":
      return t("errors.rateLimited");
    case "concurrency.conflict":
      return t("errors.concurrency");
    case "validation.failed":
      return t("errors.validationFailed");
    default:
      if (status === 403) {
        return t("errors.forbidden");
      }
      if (status === 429) {
        return t("errors.rateLimited");
      }
      return t("errors.unexpected");
  }
}
