"use client";

import Link from "../../lib/link";
import { usePathname } from "../../lib/navigation";
import { useTranslations } from "../../lib/english";
import { cn } from "../../lib/utils";
import {
  ActivityIcon,
  BlocksIcon,
  CreditCardIcon,
  GaugeIcon,
  KeyRoundIcon,
  ListChecksIcon,
  UserRoundIcon,
  WalletCardsIcon
} from "lucide-react";
import { type AdminAttention, useAdminAttention } from "./attention";

const NAV_GROUPS = [
  {
    key: "operations",
    items: [
      { key: "overview", href: "/admin", icon: GaugeIcon },
      { key: "payments", href: "/admin/payments", icon: CreditCardIcon },
      { key: "monitoring", href: "/admin/monitoring", icon: ActivityIcon },
      { key: "webhooks", href: "/admin/webhooks", icon: BlocksIcon }
    ]
  },
  {
    key: "configuration",
    items: [
      { key: "integrations", href: "/admin/integrations", icon: KeyRoundIcon },
      { key: "addresses", href: "/admin/addresses", icon: WalletCardsIcon }
    ]
  },
  {
    key: "security",
    items: [
      { key: "auditLog", href: "/admin/audit-log", icon: ListChecksIcon },
      { key: "account", href: "/admin/account", icon: UserRoundIcon }
    ]
  }
] as const;

export function AdminNav({ onNavigate, open }: { onNavigate: () => void; open: boolean }) {
  const t = useTranslations("AdminNav");
  const pathname = usePathname();
  const attention = useAdminAttention();

  return (
    <nav
      aria-label={t("ariaLabel")}
      className={cn(
        "fixed inset-y-0 left-0 top-12 z-30 w-64 overflow-y-auto border-r border-[var(--sidebar-border)] bg-[var(--sidebar)] px-3 py-4 shadow-xl lg:sticky lg:top-12 lg:block lg:h-[calc(100vh-3rem)] lg:w-auto lg:shadow-none",
        open ? "block" : "hidden lg:block"
      )}
      id="admin-navigation"
    >
      {NAV_GROUPS.map((group) => (
        <div className="mb-6 last:mb-0" key={group.key}>
          <p
            className="px-3 text-xs font-semibold tracking-wide text-[var(--muted-foreground)] uppercase"
            id={`admin-nav-${group.key}`}
          >
            {t(`groups.${group.key}`)}
          </p>
          <ul aria-labelledby={`admin-nav-${group.key}`} className="mt-2 grid gap-1">
            {group.items.map((item) => {
              const label = t(`items.${item.key}`);
              const badge = badgeFor(item.key, attention, t);
              return (
                <li key={item.key}>
                  <Link
                    // The badge sits in its own element, so the accessible name
                    // would otherwise run the two together. Saying it once,
                    // starting with the visible label, keeps both readings
                    // right.
                    aria-current={isActive(pathname, item.href) ? "page" : undefined}
                    aria-label={badge ? `${label}, ${badge.label}` : undefined}
                    className={cn(
                      "flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium",
                      isActive(pathname, item.href)
                        ? "bg-[var(--brand-soft)] text-[var(--foreground)]"
                        : "text-[var(--sidebar-foreground)] hover:bg-[var(--sidebar-accent)]"
                    )}
                    href={item.href}
                    onClick={onNavigate}
                  >
                    <item.icon aria-hidden="true" className="size-4 shrink-0" />
                    <span>{label}</span>
                    {badge ? (
                      <span
                        aria-hidden="true"
                        className="ml-auto inline-flex min-w-6 items-center justify-center rounded-md bg-[var(--destructive)] px-1.5 py-0.5 text-xs font-semibold text-[var(--primary-foreground)]"
                      >
                        {badge.text}
                      </span>
                    ) : null}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}

type NavBadgeContent = { label: string; text: string };

function badgeFor(
  itemKey: string,
  attention: AdminAttention,
  t: ReturnType<typeof useTranslations<"AdminNav">>
): NavBadgeContent | null {
  if (itemKey === "addresses") {
    return attention.addressesLowCapacity ? { label: t("lowCapacity"), text: "!" } : null;
  }

  const count =
    itemKey === "monitoring"
      ? attention.monitoring
      : itemKey === "webhooks"
        ? attention.webhooks
        : 0;
  if (count === 0) {
    return null;
  }

  return { label: `${count} ${t("needsAttention")}`, text: String(count) };
}

// `/admin` is the overview and must not light up for every page beneath it;
// every other entry owns its subtree, so a Payment detail keeps Payments marked.
function isActive(pathname: string, href: string): boolean {
  if (href === "/admin") {
    return pathname === "/admin";
  }
  return pathname === href || pathname.startsWith(`${href}/`);
}
