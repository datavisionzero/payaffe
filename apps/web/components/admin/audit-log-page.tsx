"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import Link from "../../lib/link";
import { createText } from "../../lib/text";
import { useState } from "react";
import { useEffect } from "react";
import { useSearchParams } from "../../lib/navigation";
import { type AdminAuditLogExport, exportAdminAuditLog } from "../../lib/admin-api";
import { EmptyMessage, ErrorMessage, PageHeader, Panel, StateMessage } from "./common";
import { formatDateTime } from "./format";
import { adminQueries } from "./queries";

const t = createText({
  auditActor: "Actor",
  auditEventType: "Event",
  auditLogCount: "{count, plural, one {# event} other {# events}}",
  auditLogDescription: "Recent security-relevant Admin and system events.",
  auditLogEmpty: "No Audit Log entries are available yet.",
  auditLogExport: "Export JSON",
  auditLogExported: "Exported {count, plural, one {# event} other {# events}} at {exportedAt}.",
  auditLogLoading: "Loading audit log",
  auditLogTitle: "Audit log",
  auditOccurredAt: "Occurred",
  auditOutcome: "Outcome",
  auditReasonCode: "Reason",
  auditSubject: "Subject",
  submitting: "Working"
});

export function AdminAuditLogPage() {
  const searchParams = useSearchParams();
  const [projectId, setProjectId] = useState(() => searchParams.get("projectId") ?? "");
  const projects = useQuery(adminQueries.projects());
  const query = useQuery(adminQueries.auditLog(projectId || undefined));
  const [exportResult, setExportResult] = useState<AdminAuditLogExport | null>(null);
  const exportMutation = useMutation({
    mutationFn: () => exportAdminAuditLog(projectId || undefined),
    onSuccess: (result) => {
      setExportResult(result);
      downloadAuditLogExport(result);
    }
  });
  const entries = query.data ?? [];

  useEffect(() => {
    const params = new URLSearchParams();
    if (projectId) {
      params.set("projectId", projectId);
    }
    const search = params.toString();
    window.history.replaceState(null, "", `/admin/audit-log${search ? `?${search}` : ""}`);
  }, [projectId]);

  return (
    <div className="space-y-6">
      <PageHeader
        actions={
          <>
            <span className="text-sm text-[var(--muted-foreground)]">
              {t("auditLogCount", { count: entries.length })}
            </span>
            <button
              className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:border-[var(--brand)] disabled:cursor-wait disabled:opacity-70"
              disabled={exportMutation.isPending}
              onClick={() => exportMutation.mutate()}
              type="button"
            >
              {exportMutation.isPending ? t("submitting") : t("auditLogExport")}
            </button>
          </>
        }
        description={`Installation-wide. ${t("auditLogDescription")}`}
        title={t("auditLogTitle")}
      />
      <Panel>
        <label className="block max-w-sm text-sm font-medium">
          <span>Project filter</span>
          <select
            className="mt-2 block h-10 w-full rounded-md border border-[var(--input)] bg-[var(--background)] px-3"
            onChange={(event) => {
              setExportResult(null);
              setProjectId(event.target.value);
            }}
            value={projectId}
          >
            <option value="">All Projects and installation events</option>
            {projects.data?.map((project) => (
              <option key={project.projectId} value={project.projectId}>
                {project.name}
              </option>
            ))}
          </select>
        </label>
      </Panel>
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
            <div aria-label="Audit Log table" className="overflow-x-auto" role="region" tabIndex={0}>
              <table className="w-full min-w-[900px] border-collapse text-left text-sm">
                <thead>
                  <tr className="border-b border-[var(--border)] text-[var(--muted-foreground)]">
                    <th className="py-2 pr-4 font-medium">{t("auditOccurredAt")}</th>
                    <th className="py-2 pr-4 font-medium">{t("auditEventType")}</th>
                    <th className="py-2 pr-4 font-medium">Project</th>
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
                          className="text-[var(--brand-ink)] hover:text-[var(--brand-ink)]"
                          href={`/admin/audit-log/${entry.eventId}${entry.projectId ? `?projectId=${entry.projectId}` : ""}`}
                        >
                          {entry.eventType}
                        </Link>
                      </td>
                      <td className="max-w-[180px] break-words py-3 pr-4">
                        {entry.projectId
                          ? projects.data?.find((project) => project.projectId === entry.projectId)
                              ?.name ?? entry.projectId
                          : "Installation-wide"}
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
