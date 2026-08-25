"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { type AdminAuditLogExport, exportAdminAuditLog } from "../../lib/admin-api";
import { EmptyMessage, ErrorMessage, PageHeader, Panel, StateMessage } from "./common";
import { formatDateTime } from "./format";
import { adminQueries } from "./queries";

export function AdminAuditLogPage() {
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.auditLog());
  const [exportResult, setExportResult] = useState<AdminAuditLogExport | null>(null);
  const exportMutation = useMutation({
    mutationFn: exportAdminAuditLog,
    onSuccess: (result) => {
      setExportResult(result);
      downloadAuditLogExport(result);
    }
  });
  const entries = query.data ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        actions={
          <>
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
          </>
        }
        description={t("auditLogDescription")}
        title={t("auditLogTitle")}
      />
      {query.isPending ? <StateMessage>{t("auditLogLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {exportMutation.isError ? <ErrorMessage error={exportMutation.error} /> : null}
      {exportResult ? (
        <p className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4 text-sm">
          {t("auditLogExported", {
            count: exportResult.entries.length,
            exportedAt: formatDateTime(exportResult.exportedAt)
          })}
        </p>
      ) : null}
      {!query.isPending && !query.isError ? (
        <Panel>
          {entries.length === 0 ? <EmptyMessage>{t("auditLogEmpty")}</EmptyMessage> : null}
          {entries.length > 0 ? (
            <div className="overflow-x-auto">
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
                    <tr
                      className="border-b border-[var(--border)] last:border-0"
                      key={entry.eventId}
                    >
                      <td className="py-3 pr-4">{formatDateTime(entry.occurredAt)}</td>
                      <td className="max-w-[220px] break-words py-3 pr-4 font-medium">
                        <Link
                          className="text-[var(--accent)] hover:text-[var(--accent-strong)]"
                          href={`/admin/audit-log/${entry.eventId}`}
                        >
                          {entry.eventType}
                        </Link>
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
        </Panel>
      ) : null}
    </div>
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
