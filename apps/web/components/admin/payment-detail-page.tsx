"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "../../lib/link";
import { createText } from "../../lib/text";
import { useForm } from "react-hook-form";
import {
  type AdminPaymentDetail,
  type SettlementForm,
  settleAdminPayment,
  settlementFormSchema
} from "../../lib/admin-api";
import {
  ErrorMessage,
  InfoItem,
  PageHeader,
  Panel,
  StateMessage,
  SubmitButton,
  TextField
} from "./common";
import { formatDateTime, formatFiatAmount } from "./format";
import { adminQueries } from "./queries";
import { useAdminProject } from "./project-context";
import { adminProjectPath } from "./project-routes";

const t = createText({
  backToPayments: "Back to payments",
  completedAt: "Completed",
  confirmedEligibleTotal: "Confirmed eligible total",
  expectedCryptoAmount: "Expected crypto amount",
  lateAcceptanceEndsAt: "Late acceptance ends",
  notAvailable: "Not available",
  observedTotal: "Observed total",
  payerPageId: "Payer Page ID",
  paymentAddress: "Payment address",
  paymentAmount: "Amount",
  paymentCreatedAt: "Created",
  paymentCurrency: "Currency",
  paymentCurrencyUnselected: "Not selected",
  paymentDetailDescription: "One Payment, its Blockchain Observation totals, and manual Settlement.",
  paymentDetailLoading: "Loading payment detail",
  paymentDetailTitle: "Payment detail",
  paymentExpiresAt: "Expires",
  paymentExternalReference: "External reference",
  paymentId: "Payment ID",
  paymentStatus: "Status",
  settlePayment: "Settle Payment",
  settledAt: "Settled",
  settlementReason: "Settlement reason",
  settlementTitle: "Manual Settlement",
  settlementUnavailable: "This Payment is not currently eligible for manual Settlement.",
  submitting: "Working",
  updatedAt: "Updated",
  "validation.settlementReason": "Enter a Settlement reason of at most 500 characters."
});

export function AdminPaymentDetailPage({ paymentId }: { paymentId: string }) {
  const project = useAdminProject();
  const query = useQuery(adminQueries.payment(project.projectId, paymentId));

  return (
    <div className="space-y-6">
      <PageHeader
        description={t("paymentDetailDescription")}
        title={query.data?.externalReference ?? t("paymentDetailTitle")}
      />
      <Link
        className="inline-block text-sm font-medium text-[var(--brand-ink)]"
        href={adminProjectPath(project.projectId, "/payments")}
      >
        {t("backToPayments")}
      </Link>
      {query.isPending ? <StateMessage>{t("paymentDetailLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? <PaymentDetailContent payment={query.data} /> : null}
    </div>
  );
}

function PaymentDetailContent({ payment }: { payment: AdminPaymentDetail }) {
  const project = useAdminProject();
  const projectId = project.projectId;
  const queryClient = useQueryClient();
  const form = useForm<SettlementForm>({ defaultValues: { reason: "" } });
  const mutation = useMutation({
    mutationFn: (values: SettlementForm) =>
      settleAdminPayment(projectId, payment.paymentId, payment.version, values.reason),
    onSuccess: async (result) => {
      form.reset();
      queryClient.setQueryData(adminQueries.payment(projectId, payment.paymentId).queryKey, result);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: adminQueries.payments(projectId).queryKey }),
        queryClient.invalidateQueries({ queryKey: adminQueries.auditLog(projectId).queryKey })
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
    if (
      !window.confirm(
        `Settle ${payment.externalReference}? This records a manual resolution and cannot be undone.`
      )
    ) {
      return;
    }
    mutation.mutate(parsed.data);
  });

  return (
    <>
      <Panel>
        <div className="grid gap-5 lg:grid-cols-2">
          <dl className="grid min-w-0 gap-4 sm:grid-cols-2">
            <InfoItem label={t("paymentExternalReference")} value={payment.externalReference} />
            <InfoItem label={t("paymentStatus")} value={payment.status} />
            <InfoItem
              label={t("paymentAmount")}
              value={formatFiatAmount(payment.fiatCurrency, payment.fiatAmountMinor)}
            />
            <InfoItem
              label={t("paymentCurrency")}
              value={payment.selectedCurrency ?? t("paymentCurrencyUnselected")}
            />
            <InfoItem
              label={t("expectedCryptoAmount")}
              value={payment.expectedCryptoAmount ?? t("notAvailable")}
            />
            <InfoItem label={t("observedTotal")} value={payment.observedTotal ?? t("notAvailable")} />
            <InfoItem
              label={t("confirmedEligibleTotal")}
              value={payment.confirmedEligibleTotal ?? t("notAvailable")}
            />
            <InfoItem
              label={t("paymentAddress")}
              value={payment.paymentAddress ?? t("notAvailable")}
            />
          </dl>
          <dl className="grid min-w-0 gap-4 sm:grid-cols-2">
            <InfoItem label={t("paymentId")} value={payment.paymentId} />
            <InfoItem label={t("payerPageId")} value={payment.payerPageId} />
            <InfoItem label={t("paymentCreatedAt")} value={formatDateTime(payment.createdAt)} />
            <InfoItem label={t("updatedAt")} value={formatDateTime(payment.updatedAt)} />
            <InfoItem label={t("paymentExpiresAt")} value={formatDateTime(payment.expiresAt)} />
            <InfoItem
              label={t("lateAcceptanceEndsAt")}
              value={formatDateTime(payment.lateAcceptanceEndsAt)}
            />
            <InfoItem
              label={t("completedAt")}
              value={payment.completedAt ? formatDateTime(payment.completedAt) : t("notAvailable")}
            />
            <InfoItem
              label={t("settledAt")}
              value={payment.settledAt ? formatDateTime(payment.settledAt) : t("notAvailable")}
            />
          </dl>
        </div>
      </Panel>
      <Panel>
        <h2 className="text-xl font-semibold">{t("settlementTitle")}</h2>
        {canSettle ? (
          <form className="mt-5" onSubmit={submit}>
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
          <p className="mt-3 text-sm text-[var(--muted-foreground)]">{t("settlementUnavailable")}</p>
        )}
      </Panel>
    </>
  );
}
