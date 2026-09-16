"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "../../lib/english";
import { useState } from "react";
import { useForm } from "react-hook-form";
import {
  type AdminMfaForm,
  type AdminRecoveryCodes,
  adminMfaFormSchema,
  generateAdminRecoveryCodes,
  logoutAdmin,
  stepUpAdmin
} from "../../lib/admin-api";
import {
  AdminSection,
  ErrorMessage,
  InfoItem,
  PageHeader,
  Panel,
  StateMessage,
  SubmitButton,
  TextField
} from "./common";
import { formatDateTime } from "./format";
import { adminQueries } from "./queries";

export function AdminAccountPage() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const session = useQuery(adminQueries.session());
  const [recoveryCodes, setRecoveryCodes] = useState<AdminRecoveryCodes | null>(null);
  const stepUpForm = useForm<AdminMfaForm>({ defaultValues: { totpCode: "" } });
  const logoutMutation = useMutation({
    mutationFn: logoutAdmin,
    onSuccess: () => {
      queryClient.removeQueries({
        predicate: (candidate) => candidate.queryKey[0] !== "admin-session"
      });
      queryClient.setQueryData(adminQueries.session().queryKey, null);
    }
  });
  const stepUpMutation = useMutation({
    mutationFn: stepUpAdmin,
    onSuccess: async () => {
      stepUpForm.reset({ totpCode: "" });
      await queryClient.invalidateQueries({ queryKey: adminQueries.session().queryKey });
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
      <PageHeader description={t("accountDescription")} title={t("accountTitle")} />
      <AdminSection title={t("sessionTitle")}>
        {session.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
        {session.data ? (
          <dl className="mt-5 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <InfoItem label={t("username")} value={session.data.username} />
            <InfoItem label={t("status")} value={t("authenticated")} />
            {/* Null means this account has no second factor enrolled, not that
                something is missing (ADR 0028) — so it says so rather than
                showing an empty field. */}
            <InfoItem
              label={t("mfaAuthenticatedAt")}
              value={
                session.data.mfaAuthenticatedAt === null
                  ? t("secondFactorNotEnrolled")
                  : formatDateTime(session.data.mfaAuthenticatedAt)
              }
            />
            <InfoItem
              label={t("stepUpAuthenticatedAt")}
              value={
                session.data.stepUpAuthenticatedAt === null
                  ? t("secondFactorNotEnrolled")
                  : formatDateTime(session.data.stepUpAuthenticatedAt)
              }
            />
            <InfoItem
              label={t("idleExpiresAt")}
              value={formatDateTime(session.data.idleExpiresAt)}
            />
            <InfoItem label={t("expiresAt")} value={formatDateTime(session.data.expiresAt)} />
          </dl>
        ) : null}
      </AdminSection>

      <AdminSection description={t("adminToolsDescription")} title={t("adminTools")}>
        <form className="mt-5 grid max-w-md gap-3" onSubmit={submitStepUp}>
          <TextField
            autoComplete="one-time-code"
            error={stepUpForm.formState.errors.totpCode?.message}
            inputMode="numeric"
            label={t("totpCode")}
            maxLength={6}
            pattern="[0-9]{6}"
            {...stepUpForm.register("totpCode")}
          />
          <div>
            <SubmitButton busy={stepUpMutation.isPending}>
              {stepUpMutation.isPending ? t("submitting") : t("stepUp")}
            </SubmitButton>
          </div>
          {stepUpMutation.isError ? <ErrorMessage error={stepUpMutation.error} /> : null}
        </form>
        <div className="mt-5 border-t border-[var(--border)] pt-5">
          <button
            className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--accent)] disabled:cursor-wait disabled:opacity-70"
            disabled={recoveryCodesMutation.isPending}
            onClick={() => recoveryCodesMutation.mutate()}
            type="button"
          >
            {recoveryCodesMutation.isPending ? t("submitting") : t("generateRecoveryCodes")}
          </button>
          {recoveryCodesMutation.isError ? (
            <ErrorMessage error={recoveryCodesMutation.error} />
          ) : null}
          {recoveryCodes ? (
            <RecoveryCodesPanel
              onClear={() => setRecoveryCodes(null)}
              recoveryCodes={recoveryCodes}
            />
          ) : null}
        </div>
      </AdminSection>

      <Panel>
        <button
          className="rounded-md bg-[var(--foreground)] px-4 py-2 text-sm font-semibold text-white hover:bg-black disabled:cursor-wait disabled:opacity-70"
          disabled={logoutMutation.isPending}
          onClick={() => logoutMutation.mutate()}
          type="button"
        >
          {logoutMutation.isPending ? t("submitting") : t("logout")}
        </button>
        {logoutMutation.isError ? <ErrorMessage error={logoutMutation.error} /> : null}
      </Panel>
    </div>
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
