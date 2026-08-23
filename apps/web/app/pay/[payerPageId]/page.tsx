import { PayerPage } from "../../../components/payer-page";

export default async function PaymentPage({
  params
}: {
  params: Promise<{ payerPageId: string }>;
}) {
  const { payerPageId } = await params;
  return <PayerPage payerPageId={payerPageId} />;
}
