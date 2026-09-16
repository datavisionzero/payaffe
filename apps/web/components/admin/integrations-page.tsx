"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "../../lib/english";
import { useState } from "react";
import { useForm } from "react-hook-form";
import {
  type AdminIntegrationApiCredential,
  type AdminIntegrationApiCredentialSecret,
  type CredentialForm,
  createAdminIntegrationApiCredential,
  credentialFormSchema,
  disableAdminIntegrationApiCredential,
  rotateAdminIntegrationApiCredential
} from "../../lib/admin-api";
import {
  ActionButton,
  ErrorMessage,
  PageHeader,
  Panel,
  SensitiveValuePanel,
  StateMessage,
  StatusPill,
  SubmitButton,
  TextField,
  mapFieldError
} from "./common";
import { formatDateTime } from "./format";
import { adminQueries, invalidateAdminConfiguration } from "./queries";

export function AdminIntegrationsPage() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const [secret, setSecret] = useState<AdminIntegrationApiCredentialSecret | null>(null);
  const form = useForm<CredentialForm>({ defaultValues: { name: "" } });
  const query = useQuery(adminQueries.credentials());
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
    <div className="space-y-6">
      <PageHeader description={t("credentialsDescription")} title={t("credentialsTitle")} />
      <Panel>
        <form
          className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end"
          onSubmit={submit}
        >
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
      </Panel>
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      <div className="grid gap-3">
        {query.data?.map((credential: AdminIntegrationApiCredential) => (
          <article
            className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4"
            key={credential.id}
          >
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="font-semibold">{credential.name}</h2>
                <p className="mt-1 break-all text-sm text-[var(--muted-foreground)]">
                  {credential.id}
                </p>
              </div>
              <StatusPill status={credential.status} />
            </div>
            <p className="mt-3 text-sm text-[var(--muted-foreground)]">
              {t("credentialLastUsed", {
                value: credential.lastUsedAt
                  ? formatDateTime(credential.lastUsedAt)
                  : t("notAvailable")
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
    </div>
  );
}
