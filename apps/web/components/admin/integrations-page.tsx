"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createText } from "../../lib/text";
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
import { useAdminProject } from "./project-context";
import { useWithStepUp } from "./step-up";

const t = createText({
  createCredential: "Create credential",
  credentialLastUsed: "Last used: {value}",
  credentialName: "Credential name",
  credentialToken: "New Integration API bearer token",
  credentialsDescription: "Create, rotate, and disable credentials used by external systems.",
  credentialsTitle: "Integration API Credentials",
  confirmDisable: "Disable {name}? Requests with its token are rejected from then on.",
  confirmRotate: "Rotate the token of {name}? The current token stops working immediately.",
  disableCredential: "Disable credential",
  loading: "Loading",
  notAvailable: "Not available",
  rotateCredential: "Rotate token",
  submitting: "Working",
  "validation.credentialName": "Enter a credential name of at most 255 characters."
});

export function AdminIntegrationsPage() {
  const project = useAdminProject();
  const projectId = project.projectId;
  const queryClient = useQueryClient();
  const withStepUp = useWithStepUp();
  const [secret, setSecret] = useState<AdminIntegrationApiCredentialSecret | null>(null);
  const form = useForm<CredentialForm>({ defaultValues: { name: "" } });
  const query = useQuery(adminQueries.credentials(projectId));
  const createMutation = useMutation({
    mutationFn: (name: string) => withStepUp(() => createAdminIntegrationApiCredential(projectId, name)),
    // The result carries the one-time token; it leaves the cache with the page.
    gcTime: 0,
    onSuccess: async (result) => {
      setSecret(result);
      form.reset();
      await invalidateAdminConfiguration(queryClient, projectId);
    },
    onError: (error) => mapFieldError(error, "name", form, t("validation.credentialName"))
  });
  const rotateMutation = useMutation({
    mutationFn: (credential: AdminIntegrationApiCredential) =>
      withStepUp(() => rotateAdminIntegrationApiCredential(projectId, credential)),
    gcTime: 0,
    onSuccess: async (result) => {
      setSecret(result);
      await invalidateAdminConfiguration(queryClient, projectId);
    }
  });
  const disableMutation = useMutation({
    mutationFn: (credential: AdminIntegrationApiCredential) =>
      withStepUp(() => disableAdminIntegrationApiCredential(projectId, credential)),
    onSuccess: async () => invalidateAdminConfiguration(queryClient, projectId)
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
            onClear={() => {
              setSecret(null);
              createMutation.reset();
              rotateMutation.reset();
            }}
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
                  onClick={() => {
                    if (window.confirm(t("confirmRotate", { name: credential.name }))) {
                      rotateMutation.mutate(credential);
                    }
                  }}
                >
                  {t("rotateCredential")}
                </ActionButton>
                <ActionButton
                  busy={disableMutation.isPending}
                  onClick={() => {
                    if (window.confirm(t("confirmDisable", { name: credential.name }))) {
                      disableMutation.mutate(credential);
                    }
                  }}
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
