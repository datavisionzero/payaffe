"use client";

import { useTranslations } from "../../lib/english";
import type React from "react";
import { useId } from "react";
import type { useForm } from "react-hook-form";
import { Button } from "../ui/button";
import { AdminApiError, type CredentialForm } from "../../lib/admin-api";

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
    <div className="flex flex-wrap items-end justify-between gap-3 border-b border-[var(--border)] pb-5">
      <div>
        <h1 className="text-2xl font-semibold">{title}</h1>
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
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">{title}</h2>
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
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">{children}</div>
  );
}

export function StatusPill({ status }: { status: string }) {
  return (
    <span className="inline-flex rounded-md bg-[var(--surface-strong)] px-2 py-1 text-xs font-medium">
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
      className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:border-[var(--accent)] disabled:cursor-wait disabled:opacity-70"
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
        className="mt-2 block h-11 w-full rounded-md border border-[var(--border)] bg-white px-3 text-base outline-none focus:border-[var(--accent)]"
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
    <div>
      <dt className="text-sm text-[var(--muted-foreground)]">{label}</dt>
      <dd className="mt-1 break-words text-base font-medium">{value}</dd>
    </div>
  );
}

export function StateMessage({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5 text-sm">
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
  const t = useTranslations("AdminPage");
  if (error instanceof AdminApiError) {
    const message = getAdminErrorMessage(error.code, error.status, t);
    return (
      <div
        aria-live="assertive"
        className="mt-4 rounded-md border border-[var(--danger)] bg-white p-4 text-sm text-[var(--danger)]"
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
  const t = useTranslations("AdminPage");
  return (
    <div className="mt-5 rounded-md border border-[var(--danger)] bg-white p-4" role="status">
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
  status: number,
  t: ReturnType<typeof useTranslations<"AdminPage">>
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
