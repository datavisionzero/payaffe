"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import {
  type AddressPoolImportFormInput,
  type AdminNativeEthAddressPool,
  addressPoolImportFormSchema,
  importAdminNativeEthAddressPool
} from "../../lib/admin-api";
import { ErrorMessage, InfoItem, PageHeader, Panel, StateMessage, SubmitButton } from "./common";
import { adminQueries, invalidateAdminConfiguration } from "./queries";

export function AdminAddressesPage() {
  const t = useTranslations("AdminPage");
  const queryClient = useQueryClient();
  const form = useForm<AddressPoolImportFormInput>({ defaultValues: { addresses: "" } });
  const query = useQuery(adminQueries.addressPool());
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
    <div className="space-y-6">
      <PageHeader description={t("addressPoolDescription")} title={t("addressPoolTitle")} />
      {query.isPending ? <StateMessage>{t("loading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? (
        <Panel>
          <AddressPoolSummary summary={query.data} />
        </Panel>
      ) : null}
      <Panel>
        <form onSubmit={submit}>
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
      </Panel>
    </div>
  );
}

function AddressPoolSummary({ summary }: { summary: AdminNativeEthAddressPool }) {
  const t = useTranslations("AdminPage");
  return (
    <dl className="grid gap-3 sm:grid-cols-4">
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
