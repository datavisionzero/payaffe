"use client";

import { useQuery } from "@tanstack/react-query";
import { adminQueries } from "./queries";

// The navigation carries counts so that a Reorg Alert or a stuck Webhook
// Delivery is visible from any page, not only from the page that lists it.
// These observers deliberately go stale slowly: the pages that own the same
// query keys ask for fresher data when they mount, and the counts reuse
// whatever that left in the cache.
const BADGE_STALE_TIME = 60_000;

export type AdminAttention = {
  monitoring: number;
  webhooks: number;
  addressesLowCapacity: boolean;
};

export function useAdminAttention(): AdminAttention {
  const reorgAlerts = useQuery({ ...adminQueries.reorgAlerts(), staleTime: BADGE_STALE_TIME });
  const observationHealth = useQuery({
    ...adminQueries.observationHealth(),
    staleTime: BADGE_STALE_TIME
  });
  const webhookDeliveries = useQuery({
    ...adminQueries.webhookDeliveries(),
    staleTime: BADGE_STALE_TIME
  });
  const addressPool = useQuery({ ...adminQueries.addressPool(), staleTime: BADGE_STALE_TIME });

  const degradedCurrencies = (observationHealth.data ?? []).filter(
    (health) => health.status !== "available"
  ).length;

  return {
    monitoring: (reorgAlerts.data?.length ?? 0) + degradedCurrencies,
    webhooks: webhookDeliveries.data?.length ?? 0,
    addressesLowCapacity: addressPool.data?.isLowCapacity ?? false
  };
}
