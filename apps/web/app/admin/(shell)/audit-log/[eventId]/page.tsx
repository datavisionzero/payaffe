import { AdminAuditLogDetailPage } from "../../../../../components/admin/audit-log-detail-page";

export default async function AdminAuditLogDetailRoute({
  params
}: {
  params: Promise<{ eventId: string }>;
}) {
  const { eventId } = await params;
  return <AdminAuditLogDetailPage eventId={eventId} />;
}
