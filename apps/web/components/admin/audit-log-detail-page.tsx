"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "../../lib/link";
import { createText } from "../../lib/text";
import type { AdminAuditLogEntryDetail } from "../../lib/admin-api";
import { ErrorMessage, InfoItem, PageHeader, Panel, StateMessage } from "./common";
import { formatDateTime } from "./format";
import { adminQueries } from "./queries";
import { useSearchParams } from "../../lib/navigation";
import { StepUpButton, isStepUpRequired } from "./step-up";

const t = createText({
  auditActor: "Actor",
  auditCorrelationId: "Correlation ID",
  auditEventId: "Event ID",
  auditEventType: "Event",
  auditLogDetailDescription: "One Audit Log entry with its source and correlation identifier.",
  auditLogDetailLoading: "Loading audit log detail",
  auditLogDetailTitle: "Audit log detail",
  auditOccurredAt: "Occurred",
  auditOutcome: "Outcome",
  auditReasonCode: "Reason",
  auditSourceIp: "Source IP",
  auditSourceService: "Source service",
  auditSubject: "Subject",
  auditUserAgent: "User agent",
  backToAuditLog: "Back to audit log",
  notAvailable: "Not available"
});

export function AdminAuditLogDetailPage({ eventId }: { eventId: string }) {
  const searchParams = useSearchParams();
  const projectId = searchParams.get("projectId") ?? undefined;
  const query = useQuery(adminQueries.auditLogEntry(eventId, projectId));

  return (
    <div className="space-y-6">
      <PageHeader
        description={t("auditLogDetailDescription")}
        title={query.data?.eventType ?? t("auditLogDetailTitle")}
      />
      <Link className="inline-block text-sm font-medium text-[var(--brand-ink)]" href="/admin/audit-log">
        {t("backToAuditLog")}
      </Link>
      {query.isPending ? <StateMessage>{t("auditLogDetailLoading")}</StateMessage> : null}
      {query.isError ? (
        <div>
          <ErrorMessage error={query.error} />
          {isStepUpRequired(query.error) ? (
            <StepUpButton onConfirmed={() => void query.refetch()} />
          ) : null}
        </div>
      ) : null}
      {query.data ? (
        <Panel>
          <AuditLogDetailContent entry={query.data} />
        </Panel>
      ) : null}
    </div>
  );
}

function AuditLogDetailContent({ entry }: { entry: AdminAuditLogEntryDetail }) {
  return (
    <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <InfoItem label={t("auditEventId")} value={entry.eventId} />
      <InfoItem label="Project" value={entry.projectId ?? "Installation-wide"} />
      <InfoItem label={t("auditOccurredAt")} value={formatDateTime(entry.occurredAt)} />
      <InfoItem label={t("auditEventType")} value={entry.eventType} />
      <InfoItem label={t("auditOutcome")} value={entry.outcome} />
      <InfoItem label={t("auditActor")} value={`${entry.actorType}:${entry.actorId}`} />
      <InfoItem label={t("auditSubject")} value={`${entry.subjectType}:${entry.subjectId}`} />
      <InfoItem label={t("auditReasonCode")} value={entry.reasonCode} />
      <InfoItem label={t("auditSourceService")} value={entry.sourceService} />
      <InfoItem label={t("auditSourceIp")} value={entry.sourceIp ?? t("notAvailable")} />
      <InfoItem label={t("auditUserAgent")} value={entry.userAgent ?? t("notAvailable")} />
      <InfoItem label={t("auditCorrelationId")} value={entry.correlationId} />
    </dl>
  );
}
