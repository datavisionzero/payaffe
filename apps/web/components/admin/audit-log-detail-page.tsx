"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useTranslations } from "next-intl";
import type { AdminAuditLogEntryDetail } from "../../lib/admin-api";
import { ErrorMessage, InfoItem, PageHeader, Panel, StateMessage } from "./common";
import { formatDateTime } from "./format";
import { adminQueries } from "./queries";

export function AdminAuditLogDetailPage({ eventId }: { eventId: string }) {
  const t = useTranslations("AdminPage");
  const query = useQuery(adminQueries.auditLogEntry(eventId));

  return (
    <div className="space-y-6">
      <PageHeader
        description={t("auditLogDetailDescription")}
        title={query.data?.eventType ?? t("auditLogDetailTitle")}
      />
      <Link className="inline-block text-sm font-medium text-[var(--accent)]" href="/admin/audit-log">
        {t("backToAuditLog")}
      </Link>
      {query.isPending ? <StateMessage>{t("auditLogDetailLoading")}</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {query.data ? (
        <Panel>
          <AuditLogDetailContent entry={query.data} />
        </Panel>
      ) : null}
    </div>
  );
}

function AuditLogDetailContent({ entry }: { entry: AdminAuditLogEntryDetail }) {
  const t = useTranslations("AdminPage");
  return (
    <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <InfoItem label={t("auditEventId")} value={entry.eventId} />
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
