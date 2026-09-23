"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import Link from "../../lib/link";
import { createText } from "../../lib/text";
import { useState } from "react";
import { useRouter, useSearchParams } from "../../lib/navigation";
import type React from "react";
import {
  type AdminAuditLogEntry,
  type AdminAuditLogExport,
  type AdminProject,
  exportAdminAuditLog
} from "../../lib/admin-api";
import { EmptyMessage, ErrorMessage, PageHeader, Panel, StateMessage } from "./common";
import { formatDateTime } from "./format";
import { useAdminProject } from "./project-context";
import { adminProjectPath } from "./project-routes";
import { adminQueries } from "./queries";
import { useWithStepUp } from "./step-up";

const t = createText({
  auditActor: "Actor",
  auditEventType: "Event",
  auditLogCount: "{count, plural, one {# event} other {# events}}",
  auditLogDescription: "Recent security-relevant Admin and system events.",
  auditLogInstallationDescription: "Installation-wide. Recent security-relevant Admin and system events.",
  auditLogProjectDescription: "Recent security-relevant Admin and system events of this Project.",
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
  const router = useRouter();
  const searchParams = useSearchParams();
  const [projectId, setProjectId] = useState(() => searchParams.get("projectId") ?? "");
  const projects = useQuery(adminQueries.projects());

  return (
    <AuditLogView
      description={t("auditLogInstallationDescription")}
      detailHref={(entry) =>
        `/admin/audit-log/${entry.eventId}${entry.projectId ? `?projectId=${entry.projectId}` : ""}`
      }
      filter={(clearExport) => (
        <Panel>
          <label className="block max-w-sm text-sm font-medium">
            <span>Project filter</span>
            <select
              className="mt-2 block h-10 w-full rounded-md border border-[var(--input)] bg-[var(--background)] px-3"
              onChange={(event) => {
                const next = event.target.value;
                clearExport();
                setProjectId(next);
                // Through the router, so its location and history stay in step.
                router.replace(
                  next ? `/admin/audit-log?${new URLSearchParams({ projectId: next })}` : "/admin/audit-log"
                );
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
      )}
      projectId={projectId || undefined}
      projects={projects.data}
      showProjectColumn
    />
  );
}

// The Project view is fixed to its route Project, so it needs neither the
// filter nor a Project column, and its export covers only that Project.
export function AdminProjectAuditLogPage() {
  const project = useAdminProject();

  return (
    <AuditLogView
      description={t("auditLogProjectDescription")}
      detailHref={(entry) => adminProjectPath(project.projectId, `/audit-log/${entry.eventId}`)}
      projectId={project.projectId}
      projects={[project]}
      showProjectColumn={false}
    />
  );
}

function AuditLogView({
  description,
  detailHref,
  filter,
  projectId,
  projects,
  showProjectColumn
}: {
  description: string;
  detailHref: (entry: AdminAuditLogEntry) => string;
  filter?: (clearExport: () => void) => React.ReactNode;
  projectId: string | undefined;
  projects: AdminProject[] | undefined;
  showProjectColumn: boolean;
}) {
  const withStepUp = useWithStepUp();
  const query = useQuery(adminQueries.auditLog(projectId));
  const [exportResult, setExportResult] = useState<AdminAuditLogExport | null>(null);
  const exportMutation = useMutation({
    mutationFn: () => withStepUp(() => exportAdminAuditLog(projectId)),
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
              className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:border-[var(--brand)] disabled:cursor-wait disabled:opacity-70"
              disabled={exportMutation.isPending}
              onClick={() => exportMutation.mutate()}
              type="button"
            >
              {exportMutation.isPending ? t("submitting") : t("auditLogExport")}
            </button>
          </>
        }
        description={description}
        title={t("auditLogTitle")}
      />
      {filter?.(() => setExportResult(null))}
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
                    {showProjectColumn ? <th className="py-2 pr-4 font-medium">Project</th> : null}
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
                          href={detailHref(entry)}
                        >
                          {entry.eventType}
                        </Link>
                      </td>
                      {showProjectColumn ? (
                        <td className="max-w-[180px] break-words py-3 pr-4">
                          {entry.projectId
                            ? projects?.find((project) => project.projectId === entry.projectId)
                                ?.name ?? entry.projectId
                            : "Installation-wide"}
                        </td>
                      ) : null}
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
