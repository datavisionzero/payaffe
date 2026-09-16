"use client";

import Link from "../../lib/link";
import { usePathname } from "../../lib/navigation";
import { createText } from "../../lib/text";
import { cn } from "../../lib/utils";
import {
  ActivityIcon,
  BlocksIcon,
  CreditCardIcon,
  GaugeIcon,
  FolderKanbanIcon,
  KeyRoundIcon,
  ListChecksIcon,
  UserRoundIcon,
  WalletCardsIcon
} from "lucide-react";
import { type AdminAttention, useAdminAttention } from "./attention";
import { adminProjectPath } from "./project-routes";

const t = createText({
  ariaLabel: "Admin sections",
  lowCapacity: "low capacity",
  needsAttention: "needs attention",
  "groups.operations": "Operations",
  "groups.configuration": "Configuration",
  "groups.security": "Security",
  "items.overview": "Overview",
  "items.projects": "Projects",
  "items.payments": "Payments",
  "items.monitoring": "Monitoring",
  "items.webhooks": "Webhooks",
  "items.integrations": "Integrations",
  "items.addresses": "Addresses",
  "items.auditLog": "Audit log",
  "items.account": "Account"
});

export function AdminNav({
  onNavigate,
  open,
  projectId
}: {
  onNavigate: () => void;
  open: boolean;
  projectId: string | null;
}) {
  const pathname = usePathname();
  const attention = useAdminAttention(projectId);
  const projectPath = projectId ? adminProjectPath(projectId) : null;
  const groups = [
    {
      key: "operations",
      items: [
        { key: "projects", href: "/admin/projects", icon: FolderKanbanIcon },
        ...(projectPath
          ? [
              { key: "overview", href: projectPath, icon: GaugeIcon },
              { key: "payments", href: `${projectPath}/payments`, icon: CreditCardIcon },
              { key: "monitoring", href: `${projectPath}/monitoring`, icon: ActivityIcon },
              { key: "webhooks", href: `${projectPath}/webhooks`, icon: BlocksIcon }
            ]
          : [])
      ]
    },
    ...(projectPath
      ? [
          {
            key: "configuration",
            items: [
              { key: "integrations", href: `${projectPath}/integrations`, icon: KeyRoundIcon },
              { key: "addresses", href: `${projectPath}/addresses`, icon: WalletCardsIcon }
            ]
          }
        ]
      : []),
    {
      key: "security",
      items: [
        { key: "auditLog", href: "/admin/audit-log", icon: ListChecksIcon },
        { key: "account", href: "/admin/account", icon: UserRoundIcon }
      ]
    }
  ];

  return (
    <nav
      aria-label={t("ariaLabel")}
      className={cn(
        "fixed inset-y-0 left-0 top-12 z-30 w-64 overflow-y-auto border-r border-[var(--sidebar-border)] bg-[var(--sidebar)] px-3 py-4 shadow-xl lg:sticky lg:top-12 lg:block lg:h-[calc(100vh-3rem)] lg:w-auto lg:shadow-none",
        open ? "block" : "hidden lg:block"
      )}
      id="admin-navigation"
    >
      {groups.map((group) => (
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
              const badge = badgeFor(item.key, attention);
              return (
                <li key={item.key}>
                  <Link
                    // The badge sits in its own element, so the accessible name
                    // would otherwise run the two together. Saying it once,
                    // starting with the visible label, keeps both readings
                    // right.
                    aria-current={
                      isActive(pathname, item.href, item.key === "overview" || item.key === "projects")
                        ? "page"
                        : undefined
                    }
                    aria-label={badge ? `${label}, ${badge.label}` : undefined}
                    className={cn(
                      "flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium",
                      isActive(
                        pathname,
                        item.href,
                        item.key === "overview" || item.key === "projects"
                      )
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
  attention: AdminAttention
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

// Project and overview entries are exact views; every other entry owns its
// subtree, so a Payment detail keeps Payments marked.
function isActive(pathname: string, href: string, exact = false): boolean {
  if (exact) {
    return pathname === href;
  }
  return pathname === href || pathname.startsWith(`${href}/`);
}
