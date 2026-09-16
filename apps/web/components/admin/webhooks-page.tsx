"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "../../lib/link";
import { useSearchParams } from "../../lib/navigation";
import { useTranslations } from "../../lib/english";
import { useState } from "react";
import { useForm } from "react-hook-form";
import {
  type AdminWebhookDeliveryResend,
  type AdminWebhookEndpoint,
  type WebhookEndpointForm,
  createAdminWebhookEndpoint,
  disableAdminWebhookEndpoint,
  resendAdminWebhookDelivery,
  rotateAdminWebhookEndpointSecret,
  updateAdminWebhookEndpoint,
  webhookEndpointFormSchema
} from "../../lib/admin-api";
import { cn } from "../../lib/utils";
import {
  ActionButton,
  AdminSection,
  EmptyMessage,
  ErrorMessage,
  PageHeader,
  StateMessage,
  StatusPill,
  SubmitButton,
  TextField
} from "./common";
import { formatDateTime } from "./format";
import { adminQueries, invalidateAdminConfiguration } from "./queries";

const WEBHOOK_EVENT_TYPES = [
  "payment.created",
  "payment.currency_selected",
  "payment.observed",
  "payment.completed",
  "payment.expired",
  "payment.settled"
];

export function AdminWebhooksPage() {
  const t = useTranslations("AdminPage");
  const searchParams = useSearchParams();
  const view = searchParams.get("view") === "deliveries" ? "deliveries" : "endpoints";

  return (
    <div className="space-y-6">
      <PageHeader description={t("webhooksDescription")} title={t("webhooksTitle")} />
      {/* Endpoints and Deliveries are read together while debugging a failing
          integration, so they share a page; the view stays in the URL so one
          of them can be linked to directly. */}
      <nav aria-label={t("webhookViews")}>
        <ul className="flex flex-wrap gap-2 border-b border-[var(--border)]">
          <ViewTab active={view === "endpoints"} href="/admin/webhooks">
            {t("webhookEndpointsTab")}
          </ViewTab>
          <ViewTab active={view === "deliveries"} href="/admin/webhooks?view=deliveries">
            {t("webhookDeliveriesTab")}
          </ViewTab>
        </ul>
      </nav>
      {view === "endpoints" ? <WebhookEndpointSection /> : <WebhookDeliverySection />}
    </div>
  );
}

function ViewTab({
  active,
  children,
  href
}: {
  active: boolean;
  children: React.ReactNode;
  href: "/admin/webhooks" | "/admin/webhooks?view=deliveries";
}) {
  return (
    <li>
      <Link
        aria-current={active ? "page" : undefined}
        className={cn(
          "-mb-px inline-block border-b-2 px-3 py-2 text-sm font-medium",
          active
            ? "border-[var(--brand)] text-[var(--brand-ink)]"
            : "border-transparent text-[var(--muted-foreground)] hover:text-[var(--foreground)]"
        )}
        href={href}
      >
        {children}
      </Link>
    </li>
  );
}

function WebhookEndpointSection() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const credentials = useQuery(adminQueries.credentials());
  const query = useQuery(adminQueries.webhookEndpoints());
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
    <AdminSection
      description={t("webhookEndpointsDescription")}
      title={t("webhookEndpointsTitle")}
    >
      <form className="mt-5 grid gap-4" onSubmit={submit}>
        <label className="block text-sm font-medium">
          <span>{t("credential")}</span>
          <select
            className="mt-2 block h-10 w-full rounded-md border border-[var(--input)] bg-[var(--background)] px-3"
            {...form.register("integrationApiCredentialId")}
          >
            <option value="">{t("selectCredential")}</option>
            {credentials.data
              ?.filter((credential) => credential.status !== "disabled")
              .map((credential) => (
                <option key={credential.id} value={credential.id}>
                  {credential.name}
                </option>
              ))}
          </select>
        </label>
        <TextField
          error={form.formState.errors.url?.message}
          label={t("webhookUrl")}
          {...form.register("url")}
        />
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

function EventTypeFields({
  register
}: {
  register: ReturnType<typeof useForm<WebhookEndpointForm>>["register"];
}) {
  const t = useTranslations("AdminPage");
  return (
    <fieldset>
      <legend className="text-sm font-medium">{t("eventTypes")}</legend>
      <div className="mt-2 grid gap-2 sm:grid-cols-2">
        {WEBHOOK_EVENT_TYPES.map((eventType) => (
          <label className="flex items-center gap-2 text-sm" key={eventType}>
            <input type="checkbox" value={eventType} {...register("eventTypes")} />
            <span>{eventType}</span>
          </label>
        ))}
      </div>
    </fieldset>
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
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">
            {endpoint.eventTypes.join(", ") || t("allEvents")}
          </p>
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

function WebhookDeliverySection() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const query = useQuery(adminQueries.webhookDeliveries());
  const [resentDelivery, setResentDelivery] = useState<AdminWebhookDeliveryResend | null>(null);
  const resendMutation = useMutation({
    mutationFn: resendAdminWebhookDelivery,
    onSuccess: async (result) => {
      setResentDelivery(result);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: adminQueries.webhookDeliveries().queryKey }),
        queryClient.invalidateQueries({ queryKey: adminQueries.auditLog().queryKey })
      ]);
    }
  });
  const deliveries = query.data ?? [];

  return (
    <AdminSection
      actions={
        <span className="text-sm text-[var(--muted-foreground)]">
          {t("webhookDeliveryCount", { count: deliveries.length })}
        </span>
      }
      description={t("webhookDeliveriesDescription")}
      title={t("webhookDeliveriesTitle")}
    >
      {query.isPending ? <StateMessage>{t("webhookDeliveriesLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {resendMutation.isError ? <ErrorMessage error={resendMutation.error} /> : null}
      {resentDelivery ? (
        <p className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("webhookDeliveryResent", { status: resentDelivery.deliveryStatus })}
        </p>
      ) : null}
      {!query.isPending && !query.isError && deliveries.length === 0 ? (
        <EmptyMessage>{t("webhookDeliveriesEmpty")}</EmptyMessage>
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
                <tr
                  className="border-b border-[var(--border)] last:border-0"
                  key={delivery.webhookEventId}
                >
                  <td className="max-w-[180px] break-words py-3 pr-4 font-medium">
                    {delivery.eventType}
                  </td>
                  <td className="max-w-[220px] break-words py-3 pr-4">
                    {delivery.paymentExternalReference}
                  </td>
                  <td className="py-3 pr-4">
                    <StatusPill status={delivery.status} />
                  </td>
                  <td className="py-3 pr-4">{delivery.attemptCount}</td>
                  <td className="max-w-[220px] break-words py-3 pr-4">
                    {delivery.lastSafeErrorCode ?? delivery.lastErrorCode ?? t("notAvailable")}
                  </td>
                  <td className="py-3 pr-4">
                    {delivery.lastAttemptedAt
                      ? formatDateTime(delivery.lastAttemptedAt)
                      : t("notAvailable")}
                  </td>
                  <td className="py-3">
                    <button
                      className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:border-[var(--brand)] disabled:cursor-wait disabled:opacity-70"
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
    </AdminSection>
  );
}
