"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "../../lib/link";
import { useRouter, useSearchParams } from "../../lib/navigation";
import { createText } from "../../lib/text";
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
  CancelLink,
  EmptyMessage,
  ErrorMessage,
  LinkButton,
  PageHeader,
  Panel,
  StateMessage,
  StatusPill,
  SubmitButton,
  TextField
} from "./common";
import { formatDateTime } from "./format";
import { adminQueries, invalidateAdminConfiguration } from "./queries";
import { useAdminProject } from "./project-context";
import { useWithStepUp } from "./step-up";
import { adminProjectPath } from "./project-routes";

const WEBHOOK_EVENT_TYPES = [
  "payment.created",
  "payment.currency_selected",
  "payment.observed",
  "payment.completed",
  "payment.expired",
  "payment.settled"
];
const t = createText({
  allEvents: "All supported Payment events",
  cancel: "Cancel",
  confirmDisable: "Disable the Webhook Endpoint {url}? Payment events are no longer sent to it.",
  confirmRotate: "Rotate the secret reference of {url}? Deliveries are signed with the new secret from then on.",
  createWebhookEndpoint: "Create Webhook Endpoint",
  createWebhookEndpointDescription:
    "Payment events of the selected Integration API Credential are signed and sent to this URL.",
  credential: "Integration API Credential",
  disableWebhookEndpoint: "Disable Webhook Endpoint",
  eventTypes: "Payment event types",
  loading: "Loading",
  newSecretReference: "New secret reference",
  notAvailable: "Not available",
  rotateWebhookSecret: "Rotate secret reference",
  secretReference: "Secret reference",
  secretReferenceSummary: "Secret reference: {value}",
  selectCredential: "Select a credential",
  submitting: "Working",
  updateWebhookEndpoint: "Update Webhook Endpoint",
  "validation.webhookEndpoint": "Select a credential and enter a valid URL and secret reference.",
  webhookAction: "Action",
  webhookAttempts: "Attempts",
  webhookDeliveriesDescription: "Failed or retry-pending Webhook Events that can be resent.",
  webhookDeliveriesEmpty: "No failed Webhook Deliveries are pending.",
  webhookDeliveriesLoading: "Loading Webhook Deliveries",
  webhookDeliveriesTab: "Deliveries",
  webhookDeliveriesTitle: "Webhook deliveries",
  webhookDeliveryCount: "{count, plural, one {# delivery} other {# deliveries}}",
  webhookDeliveryResent: "Resend completed with status {status}.",
  webhookEndpointsEmpty: "No Webhook Endpoints exist yet.",
  createFirstWebhookEndpoint: "Create the first Webhook Endpoint",
  webhookEndpointsDescription: "Manage the external destinations configured for Payment events.",
  webhookEndpointsTab: "Endpoints",
  webhookEndpointsTitle: "Webhook Endpoints",
  webhookEventType: "Event",
  webhookLastAttempt: "Last attempt",
  webhookLastError: "Last error",
  webhookPayment: "Payment",
  webhookResend: "Resend",
  webhookStatus: "Status",
  webhookUrl: "Webhook Endpoint URL",
  webhookViews: "Webhook views",
  webhooksDescription: "The destinations Payment events are sent to, and the deliveries that did not arrive.",
  webhooksTitle: "Webhooks"
});

export function AdminWebhooksPage() {
  const project = useAdminProject();
  const webhookPath = adminProjectPath(project.projectId, "/webhooks");
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
          <ViewTab active={view === "endpoints"} href={webhookPath}>
            {t("webhookEndpointsTab")}
          </ViewTab>
          <ViewTab active={view === "deliveries"} href={`${webhookPath}?view=deliveries`}>
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
  href: string;
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
  const project = useAdminProject();
  const createPath = adminProjectPath(project.projectId, "/webhooks/new");
  const query = useQuery(adminQueries.webhookEndpoints(project.projectId));

  return (
    <AdminSection
      actions={<LinkButton href={createPath}>{t("createWebhookEndpoint")}</LinkButton>}
      description={t("webhookEndpointsDescription")}
      title={t("webhookEndpointsTitle")}
    >
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data?.length === 0 ? (
        <EmptyMessage>
          {t("webhookEndpointsEmpty")}{" "}
          <Link className="font-medium text-[var(--brand-ink)]" href={createPath}>
            {t("createFirstWebhookEndpoint")}
          </Link>
        </EmptyMessage>
      ) : null}
      <div className="mt-5 grid gap-4">
        {query.data?.map((endpoint) => <WebhookEndpointItem endpoint={endpoint} key={endpoint.id} />)}
      </div>
    </AdminSection>
  );
}

export function AdminWebhookEndpointCreatePage() {
  const project = useAdminProject();
  const projectId = project.projectId;
  const listPath = adminProjectPath(projectId, "/webhooks");
  const router = useRouter();
  const queryClient = useQueryClient();
  const withStepUp = useWithStepUp();
  const credentials = useQuery(adminQueries.credentials(projectId));
  const form = useForm<WebhookEndpointForm>({
    defaultValues: {
      integrationApiCredentialId: "",
      url: "",
      secretReference: "",
      eventTypes: []
    }
  });
  const mutation = useMutation({
    mutationFn: (command: WebhookEndpointForm) =>
      withStepUp(() => createAdminWebhookEndpoint(projectId, command)),
    onSuccess: async () => {
      await invalidateAdminConfiguration(queryClient, projectId);
      router.push(listPath);
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
    <div className="space-y-6">
      <PageHeader
        description={t("createWebhookEndpointDescription")}
        title={t("createWebhookEndpoint")}
      />
      <Panel>
        <form className="grid gap-4" onSubmit={submit}>
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
          <div className="flex flex-wrap gap-3">
            <SubmitButton busy={mutation.isPending}>
              {mutation.isPending ? t("submitting") : t("createWebhookEndpoint")}
            </SubmitButton>
            <CancelLink href={listPath}>{t("cancel")}</CancelLink>
          </div>
        </form>
        {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
      </Panel>
    </div>
  );
}

function EventTypeFields({
  register
}: {
  register: ReturnType<typeof useForm<WebhookEndpointForm>>["register"];
}) {
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
  const project = useAdminProject();
  const projectId = project.projectId;
  const queryClient = useQueryClient();
  const withStepUp = useWithStepUp();
  const form = useForm<{ url: string; secretReference: string }>({
    defaultValues: { url: endpoint.url, secretReference: "" }
  });
  const updateMutation = useMutation({
    mutationFn: (url: string) =>
      withStepUp(() =>
        updateAdminWebhookEndpoint(projectId, endpoint, { url, eventTypes: endpoint.eventTypes })
      ),
    onSuccess: async () => invalidateAdminConfiguration(queryClient, projectId)
  });
  const rotateMutation = useMutation({
    mutationFn: (secretReference: string) =>
      withStepUp(() => rotateAdminWebhookEndpointSecret(projectId, endpoint, secretReference)),
    onSuccess: async () => {
      form.reset({ url: endpoint.url, secretReference: "" });
      await invalidateAdminConfiguration(queryClient, projectId);
    }
  });
  const disableMutation = useMutation({
    mutationFn: () => withStepUp(() => disableAdminWebhookEndpoint(projectId, endpoint)),
    onSuccess: async () => invalidateAdminConfiguration(queryClient, projectId)
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
              onClick={() => {
                if (window.confirm(t("confirmRotate", { url: endpoint.url }))) {
                  rotateMutation.mutate(form.getValues("secretReference"));
                }
              }}
            >
              {t("rotateWebhookSecret")}
            </ActionButton>
            <ActionButton
              busy={disableMutation.isPending}
              onClick={() => {
                if (window.confirm(t("confirmDisable", { url: endpoint.url }))) {
                  disableMutation.mutate();
                }
              }}
            >
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
  const project = useAdminProject();
  const projectId = project.projectId;
  const queryClient = useQueryClient();
  const withStepUp = useWithStepUp();
  const query = useQuery(adminQueries.webhookDeliveries(projectId));
  const [resentDelivery, setResentDelivery] = useState<AdminWebhookDeliveryResend | null>(null);
  const resendMutation = useMutation({
    mutationFn: (eventId: string) => withStepUp(() => resendAdminWebhookDelivery(projectId, eventId)),
    onSuccess: async (result) => {
      setResentDelivery(result);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: adminQueries.webhookDeliveries(projectId).queryKey }),
        queryClient.invalidateQueries({ queryKey: adminQueries.auditLog(projectId).queryKey })
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
        <div
          aria-label="Webhook deliveries table"
          className="mt-5 overflow-x-auto"
          role="region"
          tabIndex={0}
        >
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
                      onClick={() => {
                        if (
                          window.confirm(
                            `Resend ${delivery.eventType} for ${delivery.paymentExternalReference}?`
                          )
                        ) {
                          resendMutation.mutate(delivery.webhookEventId);
                        }
                      }}
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
