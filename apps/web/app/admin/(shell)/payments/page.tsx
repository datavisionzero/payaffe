import { Suspense } from "react";
import { AdminPaymentsPage } from "../../../../components/admin/payments-page";

export default function AdminPaymentsRoute() {
  // The filter reads the query string, which Next requires to sit behind a
  // Suspense boundary.
  return (
    <Suspense>
      <AdminPaymentsPage />
    </Suspense>
  );
}
