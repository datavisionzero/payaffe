"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import type React from "react";
import { useEffect, useId, useState } from "react";
import { useForm } from "react-hook-form";
import { Button } from "./ui/button";
import {
  AdminApiError,
  AddressPoolImportFormInput,
  AdminAuditLogExport,
  AdminAuditLogEntry,
  AdminAuditLogEntryDetail,
  AdminIntegrationApiCredential,
  AdminIntegrationApiCredentialSecret,
  AdminLoginForm,
  AdminMfaCompleteCommand,
  AdminMfaForm,
  AdminNativeEthAddressPool,
  AdminObservationHealth,
  AdminPaymentDetail,
  AdminPaymentSummary,
  AdminRecoveryCodes,
  AdminRecoveryCodeForm,
  AdminReorgAlert,
  AdminSession,
  AdminWebhookDelivery,
  AdminWebhookDeliveryResend,
  AdminWebhookEndpoint,
  CredentialForm,
  SettlementForm,
  WebhookEndpointForm,
  addressPoolImportFormSchema,
  adminLoginFormSchema,
  adminMfaFormSchema,
  adminRecoveryCodeFormSchema,
  completeAdminMfa,
  createAdminIntegrationApiCredential,
  createAdminWebhookEndpoint,
  credentialFormSchema,
  disableAdminIntegrationApiCredential,
  disableAdminWebhookEndpoint,
  exportAdminAuditLog,
  generateAdminRecoveryCodes,
  getAdminNativeEthAddressPool,
  getAdminObservationHealth,
  getAdminAuditLogEntry,
  getAdminPayment,
  getAdminSession,
  importAdminNativeEthAddressPool,
  listAdminAuditLog,
  listAdminIntegrationApiCredentials,
  listAdminPayments,
  listAdminReorgAlerts,
  listAdminWebhookDeliveries,
  listAdminWebhookEndpoints,
  logoutAdmin,
  resendAdminWebhookDelivery,
  rotateAdminIntegrationApiCredential,
  rotateAdminWebhookEndpointSecret,
  settleAdminPayment,
  settlementFormSchema,
  startAdminLogin,
  stepUpAdmin,
  updateAdminWebhookEndpoint,
  webhookEndpointFormSchema
} from "../lib/admin-api";

export function AdminPage() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ["admin-session"],
    queryFn: getAdminSession,
    retry: false
  });
  useEffect(() => {
    const expireSession = () => {
      queryClient.removeQueries({
        predicate: (candidate) => String(candidate.queryKey[0]).startsWith("admin")
      });
      queryClient.setQueryData(["admin-session"], null);
    };
    window.addEventListener("payaffe:admin-session-expired", expireSession);
    return () => window.removeEventListener("payaffe:admin-session-expired", expireSession);
  }, [queryClient]);

  return (
    <main className="mx-auto min-h-screen w-full max-w-5xl px-5 py-8 sm:px-8">
      <header className="mb-8 border-b border-[var(--border)] pb-5">
        <p className="text-sm font-medium text-[var(--accent)]">payaffe</p>
        <h1 className="mt-2 text-3xl font-semibold">{t("title")}</h1>
      </header>

      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {!query.isPending && !query.isError && query.data ? (
        <AdminSessionPanel session={query.data} />
      ) : null}
      {!query.isPending && !query.isError && !query.data ? <AdminLoginPanel /> : null}
    </main>
  );
}

function AdminLoginPanel() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const [challengeId, setChallengeId] = useState<string | null>(null);
  const [mfaMode, setMfaMode] = useState<"totp" | "recovery">("totp");
  const loginForm = useForm<AdminLoginForm>({
    defaultValues: {
      username: "",
      password: ""
    }
  });
  const mfaForm = useForm<AdminMfaForm>({
    defaultValues: {
      totpCode: ""
    }
  });
  const recoveryCodeForm = useForm<AdminRecoveryCodeForm>({
    defaultValues: {
      recoveryCode: ""
    }
  });
  const loginMutation = useMutation({
    mutationFn: startAdminLogin,
    onSuccess: async (result) => {
      loginForm.reset({ username: loginForm.getValues("username"), password: "" });

      // An account with no second factor is signed in already; the response
      // carried the session cookie and there is no second step to show
      // (ADR 0028). Refreshing the session query is what moves the UI on.
      if (result.status === "authenticated") {
        setChallengeId(null);
        await queryClient.invalidateQueries({ queryKey: ["admin-session"] });
        return;
      }

      setChallengeId(result.challengeId);
      setMfaMode("totp");
      mfaForm.reset({ totpCode: "" });
      recoveryCodeForm.reset({ recoveryCode: "" });
    }
  });
  const mfaMutation = useMutation({
    mutationFn: (command: AdminMfaCompleteCommand) => completeAdminMfa(challengeId!, command),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["admin-session"] });
    }
  });

  const submitLogin = loginForm.handleSubmit((values) => {
    const parsed = adminLoginFormSchema.safeParse(values);
    if (!parsed.success) {
      loginForm.setError("username", { message: t("validation.loginRequired") });
      return;
    }

    loginMutation.mutate(parsed.data);
  });

  const submitMfa = mfaForm.handleSubmit((values) => {
    const parsed = adminMfaFormSchema.safeParse(values);
    if (!parsed.success) {
      mfaForm.setError("totpCode", { message: t("validation.totpCode") });
      return;
    }

    mfaMutation.mutate(parsed.data);
  });
  const submitRecoveryCode = recoveryCodeForm.handleSubmit((values) => {
    const parsed = adminRecoveryCodeFormSchema.safeParse(values);
    if (!parsed.success) {
      recoveryCodeForm.setError("recoveryCode", { message: t("validation.recoveryCode") });
      return;
    }

    mfaMutation.mutate(parsed.data);
  });

  return (
    <section className="max-w-xl rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <h2 className="text-xl font-semibold">{challengeId ? t("mfaTitle") : t("loginTitle")}</h2>
      <p className="mt-2 text-sm text-[var(--muted-foreground)]">
        {challengeId ? t("mfaDescription") : t("loginDescription")}
      </p>

      {!challengeId ? (
        <form className="mt-6 space-y-4" onSubmit={submitLogin}>
          <TextField
            autoComplete="username"
            error={loginForm.formState.errors.username?.message}
            label={t("username")}
            type="email"
            {...loginForm.register("username")}
          />
          <TextField
            autoComplete="current-password"
            error={loginForm.formState.errors.password?.message}
            label={t("password")}
            type="password"
            {...loginForm.register("password")}
          />
          <SubmitButton busy={loginMutation.isPending}>
            {loginMutation.isPending ? t("submitting") : t("continue")}
          </SubmitButton>
          {loginMutation.isError ? <ErrorMessage error={loginMutation.error} /> : null}
        </form>
      ) : mfaMode === "totp" ? (
        <form className="mt-6 space-y-4" onSubmit={submitMfa}>
          <TextField
            autoComplete="one-time-code"
            error={mfaForm.formState.errors.totpCode?.message}
            inputMode="numeric"
            label={t("totpCode")}
            maxLength={6}
            pattern="[0-9]{6}"
            {...mfaForm.register("totpCode")}
          />
          <div className="flex flex-wrap gap-3">
            <SubmitButton busy={mfaMutation.isPending}>
              {mfaMutation.isPending ? t("submitting") : t("signIn")}
            </SubmitButton>
            <button
              className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--accent)]"
              onClick={() => {
                setChallengeId(null);
                mfaForm.reset({ totpCode: "" });
                recoveryCodeForm.reset({ recoveryCode: "" });
              }}
              type="button"
            >
              {t("back")}
            </button>
            <button
              className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--accent)]"
              onClick={() => {
                setMfaMode("recovery");
                mfaForm.reset({ totpCode: "" });
              }}
              type="button"
            >
              {t("useRecoveryCode")}
            </button>
          </div>
          {mfaMutation.isError ? <ErrorMessage error={mfaMutation.error} /> : null}
        </form>
      ) : (
        <form className="mt-6 space-y-4" onSubmit={submitRecoveryCode}>
          <TextField
            autoComplete="one-time-code"
            error={recoveryCodeForm.formState.errors.recoveryCode?.message}
            label={t("recoveryCode")}
            maxLength={64}
            {...recoveryCodeForm.register("recoveryCode")}
          />
          <div className="flex flex-wrap gap-3">
            <SubmitButton busy={mfaMutation.isPending}>
              {mfaMutation.isPending ? t("submitting") : t("signIn")}
            </SubmitButton>
            <button
              className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--accent)]"
              onClick={() => {
                setMfaMode("totp");
                recoveryCodeForm.reset({ recoveryCode: "" });
              }}
              type="button"
            >
              {t("useAuthenticatorCode")}
            </button>
            <button
              className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--accent)]"
              onClick={() => {
                setChallengeId(null);
                mfaForm.reset({ totpCode: "" });
                recoveryCodeForm.reset({ recoveryCode: "" });
              }}
              type="button"
            >
              {t("back")}
            </button>
          </div>
          {mfaMutation.isError ? <ErrorMessage error={mfaMutation.error} /> : null}
        </form>
      )}
    </section>
  );
}

function AdminSessionPanel({ session }: { session: AdminSession }) {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const [selectedPaymentId, setSelectedPaymentId] = useState<string | null>(null);
  const [selectedAuditLogEventId, setSelectedAuditLogEventId] = useState<string | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<AdminRecoveryCodes | null>(null);
  const stepUpForm = useForm<AdminMfaForm>({
    defaultValues: {
      totpCode: ""
    }
  });
  const paymentsQuery = useQuery({
    queryKey: ["admin-payments"],
    queryFn: listAdminPayments,
    retry: false
  });
  const auditLogQuery = useQuery({
    queryKey: ["admin-audit-log"],
    queryFn: listAdminAuditLog,
    retry: false
  });
  const webhookDeliveriesQuery = useQuery({
    queryKey: ["admin-webhook-deliveries"],
    queryFn: listAdminWebhookDeliveries,
    retry: false
  });
  const mutation = useMutation({
    mutationFn: logoutAdmin,
    onSuccess: () => {
      queryClient.removeQueries({
        predicate: (candidate) => candidate.queryKey[0] !== "admin-session"
      });
      queryClient.setQueryData(["admin-session"], null);
    }
  });
  const stepUpMutation = useMutation({
    mutationFn: stepUpAdmin,
    onSuccess: async () => {
      stepUpForm.reset({ totpCode: "" });
      await queryClient.invalidateQueries({ queryKey: ["admin-session"] });
    }
  });
  const recoveryCodesMutation = useMutation({
    mutationFn: generateAdminRecoveryCodes,
    onSuccess: (result) => {
      setRecoveryCodes(result);
    }
  });

  const submitStepUp = stepUpForm.handleSubmit((values) => {
    const parsed = adminMfaFormSchema.safeParse(values);
    if (!parsed.success) {
      stepUpForm.setError("totpCode", { message: t("validation.totpCode") });
      return;
    }

    stepUpMutation.mutate(parsed.data);
  });

  return (
    <div className="space-y-6">
      <section className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">{t("sessionTitle")}</h2>
          <dl className="mt-5 grid gap-4 sm:grid-cols-2">
            <InfoItem label={t("username")} value={session.username} />
            <InfoItem label={t("status")} value={t("authenticated")} />
            {/* Null means this account has no second factor enrolled, not that
                something is missing (ADR 0028) — so it says so rather than
                showing an empty field. */}
            <InfoItem
              label={t("mfaAuthenticatedAt")}
              value={
                session.mfaAuthenticatedAt === null
                  ? t("secondFactorNotEnrolled")
                  : formatDateTime(session.mfaAuthenticatedAt)
              }
            />
            <InfoItem
              label={t("stepUpAuthenticatedAt")}
              value={
                session.stepUpAuthenticatedAt === null
                  ? t("secondFactorNotEnrolled")
                  : formatDateTime(session.stepUpAuthenticatedAt)
              }
            />
            <InfoItem label={t("idleExpiresAt")} value={formatDateTime(session.idleExpiresAt)} />
            <InfoItem label={t("expiresAt")} value={formatDateTime(session.expiresAt)} />
          </dl>
        </div>
        <aside className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
          <h2 className="text-lg font-semibold">{t("adminTools")}</h2>
          <p className="mt-2 text-sm text-[var(--muted-foreground)]">{t("adminToolsDescription")}</p>
          <form className="mt-5 space-y-3" onSubmit={submitStepUp}>
            <TextField
              autoComplete="one-time-code"
              error={stepUpForm.formState.errors.totpCode?.message}
              inputMode="numeric"
              label={t("totpCode")}
              maxLength={6}
              pattern="[0-9]{6}"
              {...stepUpForm.register("totpCode")}
            />
            <SubmitButton busy={stepUpMutation.isPending}>
              {stepUpMutation.isPending ? t("submitting") : t("stepUp")}
            </SubmitButton>
            {stepUpMutation.isError ? <ErrorMessage error={stepUpMutation.error} /> : null}
          </form>
          <div className="mt-5 border-t border-[var(--border)] pt-5">
            <button
              className="w-full rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--accent)] disabled:cursor-wait disabled:opacity-70"
              disabled={recoveryCodesMutation.isPending}
              onClick={() => recoveryCodesMutation.mutate()}
              type="button"
            >
              {recoveryCodesMutation.isPending ? t("submitting") : t("generateRecoveryCodes")}
            </button>
            {recoveryCodesMutation.isError ? <ErrorMessage error={recoveryCodesMutation.error} /> : null}
            {recoveryCodes ? (
              <RecoveryCodesPanel
                onClear={() => setRecoveryCodes(null)}
                recoveryCodes={recoveryCodes}
              />
            ) : null}
          </div>
          <button
            className="mt-5 w-full rounded-md bg-[var(--foreground)] px-4 py-2 text-sm font-semibold text-white hover:bg-black disabled:cursor-wait disabled:opacity-70"
            disabled={mutation.isPending}
            onClick={() => mutation.mutate()}
            type="button"
          >
            {mutation.isPending ? t("submitting") : t("logout")}
          </button>
          {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
        </aside>
      </section>
      <PaymentList
        error={paymentsQuery.error}
        isError={paymentsQuery.isError}
        isPending={paymentsQuery.isPending}
        onSelectPayment={setSelectedPaymentId}
        payments={paymentsQuery.data ?? []}
        selectedPaymentId={selectedPaymentId}
      />
      {selectedPaymentId ? <PaymentDetail paymentId={selectedPaymentId} /> : null}
      <ObservationPanel />
      <ReorgAlertPanel />
      <IntegrationApiCredentialPanel />
      <WebhookEndpointPanel />
      <NativeEthAddressPoolPanel />
      <WebhookDeliveryList
        deliveries={webhookDeliveriesQuery.data ?? []}
        error={webhookDeliveriesQuery.error}
        isError={webhookDeliveriesQuery.isError}
        isPending={webhookDeliveriesQuery.isPending}
      />
      <AuditLogList
        entries={auditLogQuery.data ?? []}
        error={auditLogQuery.error}
        isError={auditLogQuery.isError}
        isPending={auditLogQuery.isPending}
        onSelectEntry={setSelectedAuditLogEventId}
        selectedEventId={selectedAuditLogEventId}
      />
      {selectedAuditLogEventId ? <AuditLogDetail eventId={selectedAuditLogEventId} /> : null}
    </div>
  );
}

function WebhookDeliveryList({
  deliveries,
  error,
  isError,
  isPending
}: {
  deliveries: AdminWebhookDelivery[];
  error: Error | null;
  isError: boolean;
  isPending: boolean;
}) {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const [resentDelivery, setResentDelivery] = useState<AdminWebhookDeliveryResend | null>(null);
  const resendMutation = useMutation({
    mutationFn: resendAdminWebhookDelivery,
    onSuccess: async (result) => {
      setResentDelivery(result);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["admin-webhook-deliveries"] }),
        queryClient.invalidateQueries({ queryKey: ["admin-audit-log"] })
      ]);
    }
  });

  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">{t("webhookDeliveriesTitle")}</h2>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">{t("webhookDeliveriesDescription")}</p>
        </div>
        <span className="text-sm text-[var(--muted-foreground)]">
          {t("webhookDeliveryCount", { count: deliveries.length })}
        </span>
      </div>
      {isPending ? <StateMessage>{t("webhookDeliveriesLoading")}</StateMessage> : null}
      {isError && error ? <ErrorMessage error={error} /> : null}
      {resendMutation.isError ? <ErrorMessage error={resendMutation.error} /> : null}
      {resentDelivery ? (
        <p className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("webhookDeliveryResent", { status: resentDelivery.deliveryStatus })}
        </p>
      ) : null}
      {!isPending && !isError && deliveries.length === 0 ? (
        <p className="mt-5 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("webhookDeliveriesEmpty")}
        </p>
      ) : null}
      {deliveries.length > 0 ? (
        <div className="mt-5 overflow-x-auto">
          <table className="w-full min-w-[980px] border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-[var(--muted-foreground)]">
                <th className="py-2 pr-4 font-medium">{t("webhookEventType")}</th>
                <th className="py-2 pr-4 font-medium">{t("webhookPayment")}</th>
                <th className="py-2 pr-4 font-medium">{t("webhookStatus")}</th>
                <th className="py-2 pr-4 font-medium">{t("webhookAttempts")}</th>
                <th className="py-2 pr-4 font-medium">{t("webhookLastError")}</th>
                <th className="py-2 pr-4 font-medium">{t("webhookLastAttempt")}</th>
                <th className="py-2 font-medium">{t("webhookAction")}</th>
              </tr>
            </thead>
            <tbody>
              {deliveries.map((delivery) => (
                <tr className="border-b border-[var(--border)] last:border-0" key={delivery.webhookEventId}>
                  <td className="max-w-[180px] break-words py-3 pr-4 font-medium">
                    {delivery.eventType}
                  </td>
                  <td className="max-w-[220px] break-words py-3 pr-4">
                    {delivery.paymentExternalReference}
                  </td>
                  <td className="py-3 pr-4">
                    <span className="inline-flex rounded-md bg-[var(--surface-strong)] px-2 py-1 text-xs font-medium">
                      {delivery.status}
                    </span>
                  </td>
                  <td className="py-3 pr-4">{delivery.attemptCount}</td>
                  <td className="max-w-[220px] break-words py-3 pr-4">
                    {delivery.lastSafeErrorCode ?? delivery.lastErrorCode ?? t("notAvailable")}
                  </td>
                  <td className="py-3 pr-4">
                    {delivery.lastAttemptedAt ? formatDateTime(delivery.lastAttemptedAt) : t("notAvailable")}
                  </td>
                  <td className="py-3">
                    <button
                      className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:border-[var(--accent)] disabled:cursor-wait disabled:opacity-70"
                      disabled={resendMutation.isPending}
                      onClick={() => resendMutation.mutate(delivery.webhookEventId)}
                      type="button"
                    >
                      {resendMutation.isPending ? t("submitting") : t("webhookResend")}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}

function RecoveryCodesPanel({
  onClear,
  recoveryCodes
}: {
  onClear: () => void;
  recoveryCodes: AdminRecoveryCodes;
}) {
  const t = useTranslations("AdminPage");
  return (
    <section className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4">
      <h3 className="text-sm font-semibold">{t("recoveryCodesTitle")}</h3>
      <p className="mt-2 text-sm text-[var(--muted-foreground)]">
        {t("recoveryCodesGeneratedAt", { generatedAt: formatDateTime(recoveryCodes.generatedAt) })}
      </p>
      <ul className="mt-3 grid gap-2">
        {recoveryCodes.recoveryCodes.map((code) => (
          <li className="rounded-md bg-white px-3 py-2 font-mono text-sm" key={code}>
            {code}
          </li>
        ))}
      </ul>
      <button
        className="mt-4 rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium"
        onClick={onClear}
        type="button"
      >
        {t("clearSensitiveValue")}
      </button>
    </section>
  );
}

function PaymentList({
  error,
  isError,
  isPending,
  onSelectPayment,
  payments,
  selectedPaymentId
}: {
  error: Error | null;
  isError: boolean;
  isPending: boolean;
  onSelectPayment: (paymentId: string) => void;
  payments: AdminPaymentSummary[];
  selectedPaymentId: string | null;
}) {
  const t = useTranslations("AdminPage");
  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">{t("paymentsTitle")}</h2>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">{t("paymentsDescription")}</p>
        </div>
        <span className="text-sm text-[var(--muted-foreground)]">
          {t("paymentCount", { count: payments.length })}
        </span>
      </div>
      {isPending ? <StateMessage>{t("paymentsLoading")}</StateMessage> : null}
      {isError && error ? <ErrorMessage error={error} /> : null}
      {!isPending && !isError && payments.length === 0 ? (
        <p className="mt-5 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("paymentsEmpty")}
        </p>
      ) : null}
      {payments.length > 0 ? (
        <div className="mt-5 overflow-x-auto">
          <table className="w-full min-w-[760px] border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-[var(--muted-foreground)]">
                <th className="py-2 pr-4 font-medium">{t("paymentExternalReference")}</th>
                <th className="py-2 pr-4 font-medium">{t("paymentStatus")}</th>
                <th className="py-2 pr-4 font-medium">{t("paymentAmount")}</th>
                <th className="py-2 pr-4 font-medium">{t("paymentCurrency")}</th>
                <th className="py-2 pr-4 font-medium">{t("paymentCreatedAt")}</th>
                <th className="py-2 font-medium">{t("paymentExpiresAt")}</th>
              </tr>
            </thead>
            <tbody>
              {payments.map((payment) => (
                <tr
                  className="border-b border-[var(--border)] last:border-0"
                  key={payment.paymentId}
                >
                  <td className="max-w-[220px] break-words py-3 pr-4 font-medium">
                    <button
                      aria-current={selectedPaymentId === payment.paymentId ? "true" : undefined}
                      className="text-left text-[var(--accent)] hover:text-[var(--accent-strong)]"
                      onClick={() => onSelectPayment(payment.paymentId)}
                      type="button"
                    >
                      {payment.externalReference}
                    </button>
                  </td>
                  <td className="py-3 pr-4">
                    <span className="inline-flex rounded-md bg-[var(--surface-strong)] px-2 py-1 text-xs font-medium">
                      {payment.status}
                    </span>
                  </td>
                  <td className="py-3 pr-4">{formatFiatAmount(payment.fiatCurrency, payment.fiatAmountMinor)}</td>
                  <td className="py-3 pr-4">{payment.selectedCurrency ?? t("paymentCurrencyUnselected")}</td>
                  <td className="py-3 pr-4">{formatDateTime(payment.createdAt)}</td>
                  <td className="py-3">{formatDateTime(payment.expiresAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}

function PaymentDetail({ paymentId }: { paymentId: string }) {
  const t = useTranslations("AdminPage");
  const query = useQuery({
    queryKey: ["admin-payment", paymentId],
    queryFn: () => getAdminPayment(paymentId),
    retry: false
  });

  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <h2 className="text-xl font-semibold">{t("paymentDetailTitle")}</h2>
      {query.isPending ? <StateMessage>{t("paymentDetailLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? <PaymentDetailContent payment={query.data} /> : null}
    </section>
  );
}

function PaymentDetailContent({ payment }: { payment: AdminPaymentDetail }) {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const form = useForm<SettlementForm>({ defaultValues: { reason: "" } });
  const mutation = useMutation({
    mutationFn: (values: SettlementForm) =>
      settleAdminPayment(payment.paymentId, payment.version, values.reason),
    onSuccess: async (result) => {
      form.reset();
      queryClient.setQueryData(["admin-payment", payment.paymentId], result);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["admin-payments"] }),
        queryClient.invalidateQueries({ queryKey: ["admin-audit-log"] })
      ]);
    }
  });
  const canSettle =
    (payment.status === "observed" || payment.status === "expired") &&
    payment.observedTotal !== null;
  const submit = form.handleSubmit((values) => {
    const parsed = settlementFormSchema.safeParse(values);
    if (!parsed.success) {
      form.setError("reason", { message: t("validation.settlementReason") });
      return;
    }
    mutation.mutate(parsed.data);
  });

  return (
    <div className="mt-5">
      <div className="grid gap-5 lg:grid-cols-2">
        <dl className="grid gap-4 sm:grid-cols-2">
        <InfoItem label={t("paymentExternalReference")} value={payment.externalReference} />
        <InfoItem label={t("paymentStatus")} value={payment.status} />
        <InfoItem label={t("paymentAmount")} value={formatFiatAmount(payment.fiatCurrency, payment.fiatAmountMinor)} />
        <InfoItem label={t("paymentCurrency")} value={payment.selectedCurrency ?? t("paymentCurrencyUnselected")} />
        <InfoItem label={t("expectedCryptoAmount")} value={payment.expectedCryptoAmount ?? t("notAvailable")} />
        <InfoItem label={t("observedTotal")} value={payment.observedTotal ?? t("notAvailable")} />
        <InfoItem label={t("confirmedEligibleTotal")} value={payment.confirmedEligibleTotal ?? t("notAvailable")} />
        <InfoItem label={t("paymentAddress")} value={payment.paymentAddress ?? t("notAvailable")} />
        </dl>
        <dl className="grid gap-4 sm:grid-cols-2">
        <InfoItem label={t("paymentId")} value={payment.paymentId} />
        <InfoItem label={t("payerPageId")} value={payment.payerPageId} />
        <InfoItem label={t("paymentCreatedAt")} value={formatDateTime(payment.createdAt)} />
        <InfoItem label={t("updatedAt")} value={formatDateTime(payment.updatedAt)} />
        <InfoItem label={t("paymentExpiresAt")} value={formatDateTime(payment.expiresAt)} />
        <InfoItem label={t("lateAcceptanceEndsAt")} value={formatDateTime(payment.lateAcceptanceEndsAt)} />
        <InfoItem label={t("completedAt")} value={payment.completedAt ? formatDateTime(payment.completedAt) : t("notAvailable")} />
          <InfoItem label={t("settledAt")} value={payment.settledAt ? formatDateTime(payment.settledAt) : t("notAvailable")} />
        </dl>
      </div>
      {canSettle ? (
        <form className="mt-6 border-t border-[var(--border)] pt-5" onSubmit={submit}>
          <TextField
            error={form.formState.errors.reason?.message}
            label={t("settlementReason")}
            maxLength={500}
            {...form.register("reason")}
          />
          <div className="mt-3">
            <SubmitButton busy={mutation.isPending}>
              {mutation.isPending ? t("submitting") : t("settlePayment")}
            </SubmitButton>
          </div>
          {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
        </form>
      ) : (
        <p className="mt-6 border-t border-[var(--border)] pt-4 text-sm text-[var(--muted-foreground)]">
          {t("settlementUnavailable")}
        </p>
      )}
    </div>
  );
}

function AuditLogList({
  entries,
  error,
  isError,
  isPending,
  onSelectEntry,
  selectedEventId
}: {
  entries: AdminAuditLogEntry[];
  error: Error | null;
  isError: boolean;
  isPending: boolean;
  onSelectEntry: (eventId: string) => void;
  selectedEventId: string | null;
}) {
  const t = useTranslations("AdminPage");
  const [exportResult, setExportResult] = useState<AdminAuditLogExport | null>(null);
  const exportMutation = useMutation({
    mutationFn: exportAdminAuditLog,
    onSuccess: (result) => {
      setExportResult(result);
      downloadAuditLogExport(result);
    }
  });

  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">{t("auditLogTitle")}</h2>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">{t("auditLogDescription")}</p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-sm text-[var(--muted-foreground)]">
            {t("auditLogCount", { count: entries.length })}
          </span>
          <button
            className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:border-[var(--accent)] disabled:cursor-wait disabled:opacity-70"
            disabled={exportMutation.isPending}
            onClick={() => exportMutation.mutate()}
            type="button"
          >
            {exportMutation.isPending ? t("submitting") : t("auditLogExport")}
          </button>
        </div>
      </div>
      {isPending ? <StateMessage>{t("auditLogLoading")}</StateMessage> : null}
      {isError && error ? <ErrorMessage error={error} /> : null}
      {exportMutation.isError ? <ErrorMessage error={exportMutation.error} /> : null}
      {exportResult ? (
        <p className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("auditLogExported", {
            count: exportResult.entries.length,
            exportedAt: formatDateTime(exportResult.exportedAt)
          })}
        </p>
      ) : null}
      {!isPending && !isError && entries.length === 0 ? (
        <p className="mt-5 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("auditLogEmpty")}
        </p>
      ) : null}
      {entries.length > 0 ? (
        <div className="mt-5 overflow-x-auto">
          <table className="w-full min-w-[900px] border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-[var(--muted-foreground)]">
                <th className="py-2 pr-4 font-medium">{t("auditOccurredAt")}</th>
                <th className="py-2 pr-4 font-medium">{t("auditEventType")}</th>
                <th className="py-2 pr-4 font-medium">{t("auditOutcome")}</th>
                <th className="py-2 pr-4 font-medium">{t("auditActor")}</th>
                <th className="py-2 pr-4 font-medium">{t("auditSubject")}</th>
                <th className="py-2 font-medium">{t("auditReasonCode")}</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr className="border-b border-[var(--border)] last:border-0" key={entry.eventId}>
                  <td className="py-3 pr-4">{formatDateTime(entry.occurredAt)}</td>
                  <td className="max-w-[220px] break-words py-3 pr-4 font-medium">
                    <button
                      aria-current={selectedEventId === entry.eventId ? "true" : undefined}
                      className="text-left text-[var(--accent)] hover:text-[var(--accent-strong)]"
                      onClick={() => onSelectEntry(entry.eventId)}
                      type="button"
                    >
                      {entry.eventType}
                    </button>
                  </td>
                  <td className="py-3 pr-4">
                    <span className="inline-flex rounded-md bg-[var(--surface-strong)] px-2 py-1 text-xs font-medium">
                      {entry.outcome}
                    </span>
                  </td>
                  <td className="max-w-[220px] break-words py-3 pr-4">
                    {entry.actorType}:{entry.actorId}
                  </td>
                  <td className="max-w-[220px] break-words py-3 pr-4">
                    {entry.subjectType}:{entry.subjectId}
                  </td>
                  <td className="max-w-[220px] break-words py-3">{entry.reasonCode}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}

function downloadAuditLogExport(exportResult: AdminAuditLogExport) {
  if (typeof window === "undefined" || typeof URL.createObjectURL !== "function") {
    return;
  }

  const blob = new Blob([JSON.stringify(exportResult, null, 2)], {
    type: "application/json"
  });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `payaffe-audit-log-${exportResult.exportedAt.replaceAll(":", "-")}.json`;
  anchor.click();
  URL.revokeObjectURL(url);
}

function AuditLogDetail({ eventId }: { eventId: string }) {
  const t = useTranslations("AdminPage");
  const query = useQuery({
    queryKey: ["admin-audit-log-entry", eventId],
    queryFn: () => getAdminAuditLogEntry(eventId),
    retry: false
  });

  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <h2 className="text-xl font-semibold">{t("auditLogDetailTitle")}</h2>
      {query.isPending ? <StateMessage>{t("auditLogDetailLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? <AuditLogDetailContent entry={query.data} /> : null}
    </section>
  );
}

function AuditLogDetailContent({ entry }: { entry: AdminAuditLogEntryDetail }) {
  const t = useTranslations("AdminPage");
  return (
    <dl className="mt-5 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <InfoItem label={t("auditEventId")} value={entry.eventId} />
      <InfoItem label={t("auditOccurredAt")} value={formatDateTime(entry.occurredAt)} />
      <InfoItem label={t("auditEventType")} value={entry.eventType} />
      <InfoItem label={t("auditOutcome")} value={entry.outcome} />
      <InfoItem label={t("auditActor")} value={`${entry.actorType}:${entry.actorId}`} />
      <InfoItem label={t("auditSubject")} value={`${entry.subjectType}:${entry.subjectId}`} />
      <InfoItem label={t("auditReasonCode")} value={entry.reasonCode} />
      <InfoItem label={t("auditSourceService")} value={entry.sourceService} />
      <InfoItem label={t("auditSourceIp")} value={entry.sourceIp ?? t("notAvailable")} />
      <InfoItem label={t("auditUserAgent")} value={entry.userAgent ?? t("notAvailable")} />
      <InfoItem label={t("auditCorrelationId")} value={entry.correlationId} />
    </dl>
  );
}

function ObservationPanel() {
  const t = useTranslations("AdminPage");
  const query = useQuery({
    queryKey: ["admin-observation-health"],
    queryFn: getAdminObservationHealth,
    refetchInterval: 30_000,
    retry: false
  });

  return (
    <AdminSection description={t("observationHealthDescription")} title={t("observationHealthTitle")}>
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? (
        <div className="mt-5 grid gap-3 sm:grid-cols-3">
          {query.data.map((health: AdminObservationHealth) => (
            <article className="rounded-md border border-[var(--border)] p-4" key={health.supportedCurrency}>
              <div className="flex items-center justify-between gap-3">
                <h3 className="font-semibold">{health.supportedCurrency}</h3>
                <StatusPill status={health.status} />
              </div>
              <dl className="mt-3 grid gap-2 text-sm">
                <InfoItem label={t("provider")} value={health.providerName} />
                <InfoItem
                  label={t("lastSuccess")}
                  value={health.lastSuccessfulAt ? formatDateTime(health.lastSuccessfulAt) : t("notAvailable")}
                />
                <InfoItem
                  label={t("lastFailure")}
                  value={health.lastFailedAt ? formatDateTime(health.lastFailedAt) : t("notAvailable")}
                />
                <InfoItem label={t("safeErrorCode")} value={health.lastSafeErrorCode ?? t("notAvailable")} />
              </dl>
            </article>
          ))}
        </div>
      ) : null}
    </AdminSection>
  );
}

function ReorgAlertPanel() {
  const t = useTranslations("AdminPage");
  const query = useQuery({
    queryKey: ["admin-reorg-alerts"],
    queryFn: listAdminReorgAlerts,
    retry: false
  });

  return (
    <AdminSection description={t("reorgAlertsDescription")} title={t("reorgAlertsTitle")}>
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data?.length === 0 ? <StateMessage>{t("reorgAlertsEmpty")}</StateMessage> : null}
      {query.data?.map((alert: AdminReorgAlert) => (
        <article className="mt-4 rounded-md border border-[var(--border)] p-4" key={alert.id}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h3 className="font-semibold">{alert.supportedCurrency} · {alert.paymentId}</h3>
            <StatusPill status={alert.status} />
          </div>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <InfoItem label={t("transactionHash")} value={alert.transactionHash} />
            <InfoItem label={t("previousConfirmations")} value={String(alert.previousConfirmations)} />
            <InfoItem label={t("newConfirmations")} value={String(alert.newConfirmations)} />
            <InfoItem label={t("updatedAt")} value={formatDateTime(alert.updatedAt)} />
          </dl>
        </article>
      ))}
    </AdminSection>
  );
}

function IntegrationApiCredentialPanel() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const [secret, setSecret] = useState<AdminIntegrationApiCredentialSecret | null>(null);
  const form = useForm<CredentialForm>({ defaultValues: { name: "" } });
  const query = useQuery({
    queryKey: ["admin-integration-api-credentials"],
    queryFn: listAdminIntegrationApiCredentials,
    retry: false
  });
  const createMutation = useMutation({
    mutationFn: createAdminIntegrationApiCredential,
    onSuccess: async (result) => {
      setSecret(result);
      form.reset();
      await invalidateAdminConfiguration(queryClient);
    },
    onError: (error) => mapFieldError(error, "name", form, t("validation.credentialName"))
  });
  const rotateMutation = useMutation({
    mutationFn: rotateAdminIntegrationApiCredential,
    onSuccess: async (result) => {
      setSecret(result);
      await invalidateAdminConfiguration(queryClient);
    }
  });
  const disableMutation = useMutation({
    mutationFn: disableAdminIntegrationApiCredential,
    onSuccess: async () => invalidateAdminConfiguration(queryClient)
  });
  const submit = form.handleSubmit((values) => {
    const parsed = credentialFormSchema.safeParse(values);
    if (!parsed.success) {
      form.setError("name", { message: t("validation.credentialName") });
      return;
    }
    createMutation.mutate(parsed.data.name);
  });

  return (
    <AdminSection
      description={t("credentialsDescription")}
      title={t("credentialsTitle")}
    >
      <form className="mt-5 grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end" onSubmit={submit}>
        <TextField
          error={form.formState.errors.name?.message}
          label={t("credentialName")}
          maxLength={255}
          {...form.register("name")}
        />
        <SubmitButton busy={createMutation.isPending}>
          {createMutation.isPending ? t("submitting") : t("createCredential")}
        </SubmitButton>
      </form>
      {createMutation.isError ? <ErrorMessage error={createMutation.error} /> : null}
      {rotateMutation.isError ? <ErrorMessage error={rotateMutation.error} /> : null}
      {disableMutation.isError ? <ErrorMessage error={disableMutation.error} /> : null}
      {secret ? (
        <SensitiveValuePanel
          label={t("credentialToken")}
          onClear={() => setSecret(null)}
          value={secret.token}
        />
      ) : null}
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      <div className="mt-5 grid gap-3">
        {query.data?.map((credential: AdminIntegrationApiCredential) => (
          <article className="rounded-md border border-[var(--border)] p-4" key={credential.id}>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h3 className="font-semibold">{credential.name}</h3>
                <p className="mt-1 break-all text-sm text-[var(--muted-foreground)]">{credential.id}</p>
              </div>
              <StatusPill status={credential.status} />
            </div>
            <p className="mt-3 text-sm text-[var(--muted-foreground)]">
              {t("credentialLastUsed", {
                value: credential.lastUsedAt ? formatDateTime(credential.lastUsedAt) : t("notAvailable")
              })}
            </p>
            {credential.status !== "disabled" ? (
              <div className="mt-4 flex flex-wrap gap-2">
                <ActionButton
                  busy={rotateMutation.isPending}
                  onClick={() => rotateMutation.mutate(credential)}
                >
                  {t("rotateCredential")}
                </ActionButton>
                <ActionButton
                  busy={disableMutation.isPending}
                  onClick={() => disableMutation.mutate(credential)}
                >
                  {t("disableCredential")}
                </ActionButton>
              </div>
            ) : null}
          </article>
        ))}
      </div>
    </AdminSection>
  );
}

function WebhookEndpointPanel() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const credentials = useQuery({
    queryKey: ["admin-integration-api-credentials"],
    queryFn: listAdminIntegrationApiCredentials,
    retry: false
  });
  const query = useQuery({
    queryKey: ["admin-webhook-endpoints"],
    queryFn: listAdminWebhookEndpoints,
    retry: false
  });
  const form = useForm<WebhookEndpointForm>({
    defaultValues: {
      integrationApiCredentialId: "",
      url: "",
      secretReference: "",
      eventTypes: []
    }
  });
  const mutation = useMutation({
    mutationFn: createAdminWebhookEndpoint,
    onSuccess: async () => {
      form.reset();
      await invalidateAdminConfiguration(queryClient);
    }
  });
  const submit = form.handleSubmit((values) => {
    const parsed = webhookEndpointFormSchema.safeParse(values);
    if (!parsed.success) {
      form.setError("url", { message: t("validation.webhookEndpoint") });
      return;
    }
    mutation.mutate(parsed.data);
  });

  return (
    <AdminSection description={t("webhookEndpointsDescription")} title={t("webhookEndpointsTitle")}>
      <form className="mt-5 grid gap-4" onSubmit={submit}>
        <label className="block text-sm font-medium">
          <span>{t("credential")}</span>
          <select
            className="mt-2 block h-11 w-full rounded-md border border-[var(--border)] bg-white px-3"
            {...form.register("integrationApiCredentialId")}
          >
            <option value="">{t("selectCredential")}</option>
            {credentials.data?.filter((credential) => credential.status !== "disabled").map((credential) => (
              <option key={credential.id} value={credential.id}>{credential.name}</option>
            ))}
          </select>
        </label>
        <TextField error={form.formState.errors.url?.message} label={t("webhookUrl")} {...form.register("url")} />
        <TextField
          error={form.formState.errors.secretReference?.message}
          label={t("secretReference")}
          {...form.register("secretReference")}
        />
        <EventTypeFields register={form.register} />
        <SubmitButton busy={mutation.isPending}>
          {mutation.isPending ? t("submitting") : t("createWebhookEndpoint")}
        </SubmitButton>
      </form>
      {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      <div className="mt-5 grid gap-4">
        {query.data?.map((endpoint) => <WebhookEndpointItem endpoint={endpoint} key={endpoint.id} />)}
      </div>
    </AdminSection>
  );
}

function WebhookEndpointItem({ endpoint }: { endpoint: AdminWebhookEndpoint }) {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const form = useForm<{ url: string; secretReference: string }>({
    defaultValues: { url: endpoint.url, secretReference: "" }
  });
  const updateMutation = useMutation({
    mutationFn: (url: string) =>
      updateAdminWebhookEndpoint(endpoint, { url, eventTypes: endpoint.eventTypes }),
    onSuccess: async () => invalidateAdminConfiguration(queryClient)
  });
  const rotateMutation = useMutation({
    mutationFn: (secretReference: string) =>
      rotateAdminWebhookEndpointSecret(endpoint, secretReference),
    onSuccess: async () => {
      form.reset({ url: endpoint.url, secretReference: "" });
      await invalidateAdminConfiguration(queryClient);
    }
  });
  const disableMutation = useMutation({
    mutationFn: () => disableAdminWebhookEndpoint(endpoint),
    onSuccess: async () => invalidateAdminConfiguration(queryClient)
  });

  return (
    <article className="rounded-md border border-[var(--border)] p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="break-all font-semibold">{endpoint.url}</h3>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">{endpoint.eventTypes.join(", ") || t("allEvents")}</p>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">
            {t("secretReferenceSummary", { value: endpoint.secretReference })}
          </p>
        </div>
        <StatusPill status={endpoint.status} />
      </div>
      {endpoint.status !== "disabled" ? (
        <div className="mt-4 grid gap-3">
          <TextField label={t("webhookUrl")} {...form.register("url")} />
          <ActionButton
            busy={updateMutation.isPending}
            onClick={() => updateMutation.mutate(form.getValues("url"))}
          >
            {t("updateWebhookEndpoint")}
          </ActionButton>
          <TextField label={t("newSecretReference")} {...form.register("secretReference")} />
          <div className="flex flex-wrap gap-2">
            <ActionButton
              busy={rotateMutation.isPending}
              onClick={() => rotateMutation.mutate(form.getValues("secretReference"))}
            >
              {t("rotateWebhookSecret")}
            </ActionButton>
            <ActionButton busy={disableMutation.isPending} onClick={() => disableMutation.mutate()}>
              {t("disableWebhookEndpoint")}
            </ActionButton>
          </div>
        </div>
      ) : null}
      {updateMutation.isError ? <ErrorMessage error={updateMutation.error} /> : null}
      {rotateMutation.isError ? <ErrorMessage error={rotateMutation.error} /> : null}
      {disableMutation.isError ? <ErrorMessage error={disableMutation.error} /> : null}
    </article>
  );
}

function NativeEthAddressPoolPanel() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const form = useForm<AddressPoolImportFormInput>({ defaultValues: { addresses: "" } });
  const query = useQuery({
    queryKey: ["admin-native-eth-address-pool"],
    queryFn: getAdminNativeEthAddressPool,
    retry: false
  });
  const mutation = useMutation({
    mutationFn: importAdminNativeEthAddressPool,
    onSuccess: async () => {
      form.reset();
      await invalidateAdminConfiguration(queryClient);
    }
  });
  const submit = form.handleSubmit((values) => {
    const parsed = addressPoolImportFormSchema.safeParse(values);
    if (parsed.success) {
      mutation.mutate(parsed.data.addresses);
    } else {
      form.setError("addresses", { message: t("validation.addressPool") });
    }
  });

  return (
    <AdminSection description={t("addressPoolDescription")} title={t("addressPoolTitle")}>
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? <AddressPoolSummary summary={query.data} /> : null}
      <form className="mt-5" onSubmit={submit}>
        <label className="block text-sm font-medium">
          <span>{t("addressPoolAddresses")}</span>
          <textarea
            aria-describedby={form.formState.errors.addresses ? "address-pool-error" : undefined}
            aria-invalid={form.formState.errors.addresses ? "true" : undefined}
            className="mt-2 block min-h-36 w-full rounded-md border border-[var(--border)] bg-white p-3 font-mono text-sm"
            {...form.register("addresses")}
          />
        </label>
        <p className="mt-2 text-sm text-[var(--muted-foreground)]">{t("addressPoolInputHelp")}</p>
        <div className="mt-3">
          <SubmitButton busy={mutation.isPending}>
            {mutation.isPending ? t("submitting") : t("importAddresses")}
          </SubmitButton>
        </div>
      </form>
      {form.formState.errors.addresses ? (
        <p className="mt-2 text-sm text-[var(--danger)]" id="address-pool-error">
          {form.formState.errors.addresses.message}
        </p>
      ) : null}
      {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
    </AdminSection>
  );
}

function AddressPoolSummary({ summary }: { summary: AdminNativeEthAddressPool }) {
  const t = useTranslations("AdminPage");
  return (
    <dl className="mt-5 grid gap-3 sm:grid-cols-4">
      <InfoItem label={t("unusedAddresses")} value={String(summary.unusedCount)} />
      <InfoItem label={t("assignedAddresses")} value={String(summary.assignedCount)} />
      <InfoItem label={t("retiredAddresses")} value={String(summary.retiredCount)} />
      <InfoItem
        label={t("capacity")}
        value={summary.isLowCapacity ? t("lowCapacity") : t("capacityAvailable")}
      />
    </dl>
  );
}

const webhookEventTypes = [
  "payment.created",
  "payment.currency_selected",
  "payment.observed",
  "payment.completed",
  "payment.expired",
  "payment.settled"
];

function EventTypeFields({ register }: { register: ReturnType<typeof useForm<WebhookEndpointForm>>["register"] }) {
  const t = useTranslations("AdminPage");
  return (
    <fieldset>
      <legend className="text-sm font-medium">{t("eventTypes")}</legend>
      <div className="mt-2 grid gap-2 sm:grid-cols-2">
        {webhookEventTypes.map((eventType) => (
          <label className="flex items-center gap-2 text-sm" key={eventType}>
            <input type="checkbox" value={eventType} {...register("eventTypes")} />
            <span>{eventType}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function SensitiveValuePanel({
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

function AdminSection({
  children,
  description,
  title
}: {
  children: React.ReactNode;
  description: string;
  title: string;
}) {
  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5">
      <h2 className="text-xl font-semibold">{title}</h2>
      <p className="mt-1 text-sm text-[var(--muted-foreground)]">{description}</p>
      {children}
    </section>
  );
}

function StatusPill({ status }: { status: string }) {
  return (
    <span className="inline-flex rounded-md bg-[var(--surface-strong)] px-2 py-1 text-xs font-medium">
      {status}
    </span>
  );
}

function ActionButton({
  busy,
  children,
  onClick
}: {
  busy: boolean;
  children: React.ReactNode;
  onClick: () => void;
}) {
  return (
    <Button
      disabled={busy}
      onClick={onClick}
      size="lg"
      type="button"
      variant="outline"
    >
      {children}
    </Button>
  );
}

async function invalidateAdminConfiguration(queryClient: ReturnType<typeof useQueryClient>) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ["admin-integration-api-credentials"] }),
    queryClient.invalidateQueries({ queryKey: ["admin-webhook-endpoints"] }),
    queryClient.invalidateQueries({ queryKey: ["admin-native-eth-address-pool"] }),
    queryClient.invalidateQueries({ queryKey: ["admin-audit-log"] })
  ]);
}

function mapFieldError(
  error: Error,
  field: string,
  form: ReturnType<typeof useForm<CredentialForm>>,
  message: string
) {
  if (error instanceof AdminApiError && error.fieldErrors[field]) {
    form.setError("name", { message });
  }
}

function TextField({
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

function SubmitButton({ busy, children }: { busy: boolean; children: React.ReactNode }) {
  return (
    <Button
      className="h-10 px-4"
      disabled={busy}
      type="submit"
    >
      {children}
    </Button>
  );
}

function InfoItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-sm text-[var(--muted-foreground)]">{label}</dt>
      <dd className="mt-1 break-words text-base font-medium">{value}</dd>
    </div>
  );
}

function StateMessage({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-5 text-sm">
      {children}
    </div>
  );
}

function ErrorMessage({ error }: { error: Error }) {
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

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString(clientLocale());
}

function formatFiatAmount(currency: string, minorUnits: number | string): string {
  return new Intl.NumberFormat(clientLocale(), {
    style: "currency",
    currency
  }).format(Number(minorUnits) / 100);
}

function getAdminErrorMessage(
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

function clientLocale(): string {
  return typeof document === "undefined" ? "en" : document.documentElement.lang || "en";
}
