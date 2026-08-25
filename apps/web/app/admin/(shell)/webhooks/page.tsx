import { Suspense } from "react";
import { AdminWebhooksPage } from "../../../../components/admin/webhooks-page";

export default function AdminWebhooksRoute() {
  // The selected view lives in the query string, which Next requires to sit
  // behind a Suspense boundary.
  return (
    <Suspense>
      <AdminWebhooksPage />
    </Suspense>
  );
}
