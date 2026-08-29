"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import type { AdminSession } from "../../lib/admin-api";
import { ErrorMessage, StateMessage } from "./common";
import { AdminNav } from "./admin-nav";
import { adminQueries } from "./queries";

export function AdminShell({ children }: { children: React.ReactNode }) {
  const t = useTranslations("AdminNav");
  const router = useRouter();
  const pathname = usePathname();
  const queryClient = useQueryClient();
  const [navOpen, setNavOpen] = useState(false);
  const session = useQuery(adminQueries.session());

  useEffect(() => {
    const expireSession = () => {
      queryClient.removeQueries({
        predicate: (candidate) => String(candidate.queryKey[0]).startsWith("admin")
      });
      queryClient.setQueryData(adminQueries.session().queryKey, null);
    };
    window.addEventListener("payaffe:admin-session-expired", expireSession);
    return () => window.removeEventListener("payaffe:admin-session-expired", expireSession);
  }, [queryClient]);

  // A guard here is UX, not authorization: the backend rejects the request
  // regardless of what this renders (ADR 0022). Sending the browser to the
  // sign-in route is what keeps a signed-out admin from staring at empty
  // panels that each report their own 401.
  const signedOut = !session.isPending && !session.isError && !session.data;
  useEffect(() => {
    if (signedOut) {
      router.replace("/admin/login");
    }
  }, [router, signedOut]);

  // The panel is a disclosure on small screens, and a link inside it navigates
  // without unmounting the shell, so it has to be closed by hand.
  useEffect(() => {
    setNavOpen(false);
  }, [pathname]);

  if (session.isPending) {
    return <CenteredState>{t("checkingSession")}</CenteredState>;
  }

  if (session.isError) {
    return (
      <div className="mx-auto w-full max-w-xl px-5 py-12">
        <ErrorMessage error={session.error} />
      </div>
    );
  }

  if (!session.data) {
    return <CenteredState>{t("redirecting")}</CenteredState>;
  }

  return (
    <div className="min-h-screen">
      <a
        className="sr-only rounded-md bg-[var(--foreground)] px-4 py-2 text-sm font-medium text-white focus:not-sr-only focus:absolute focus:top-3 focus:left-3 focus:z-20"
        href="#admin-main"
      >
        {t("skipToContent")}
      </a>
      <TopBar
        navOpen={navOpen}
        onToggleNav={() => setNavOpen((open) => !open)}
        session={session.data}
      />
      <div className="lg:grid lg:grid-cols-[16rem_minmax(0,1fr)] lg:items-start">
        <AdminNav onNavigate={() => setNavOpen(false)} open={navOpen} />
        <main className="min-w-0 px-5 py-8 sm:px-8" id="admin-main">
          {children}
        </main>
      </div>
    </div>
  );
}

function TopBar({
  navOpen,
  onToggleNav,
  session
}: {
  navOpen: boolean;
  onToggleNav: () => void;
  session: AdminSession;
}) {
  const t = useTranslations("AdminNav");
  return (
    <header className="sticky top-0 z-10 flex h-14 items-center justify-between gap-3 border-b border-[var(--border)] bg-[var(--surface)] px-4">
      <div className="flex items-center gap-3">
        <button
          aria-controls="admin-navigation"
          aria-expanded={navOpen}
          className="rounded-md border border-[var(--border)] px-3 py-1.5 text-sm font-medium lg:hidden"
          onClick={onToggleNav}
          type="button"
        >
          {t("menu")}
        </button>
        <Link className="text-sm font-semibold text-[var(--accent)]" href="/admin">
          payaffe
        </Link>
      </div>
      <Link
        className="max-w-[50%] truncate text-sm font-medium hover:text-[var(--accent)]"
        href="/admin/account"
      >
        <span className="sr-only">{t("signedInAs")} </span>
        {session.username}
      </Link>
    </header>
  );
}

function CenteredState({ children }: { children: React.ReactNode }) {
  return (
    <div className="mx-auto flex min-h-screen w-full max-w-xl items-center px-5">
      <div className="w-full">
        <StateMessage>{children}</StateMessage>
      </div>
    </div>
  );
}
