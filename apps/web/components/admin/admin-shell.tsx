"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import {
  defaultAdminProjectId,
  setSelectedAdminProjectId,
  type AdminProject,
  type AdminSession
} from "../../lib/admin-api";
import { ErrorMessage, StateMessage } from "./common";
import { AdminNav } from "./admin-nav";
import { adminQueries } from "./queries";

export function AdminShell({ children }: { children: React.ReactNode }) {
  const t = useTranslations("AdminNav");
  const router = useRouter();
  const pathname = usePathname();
  const queryClient = useQueryClient();
  const [navOpen, setNavOpen] = useState(false);
  const [selectedProjectId, setSelectedProjectId] = useState(defaultAdminProjectId);
  const session = useQuery(adminQueries.session());
  const projects = useQuery({
    ...adminQueries.projects(),
    enabled: Boolean(session.data)
  });

  useEffect(() => {
    if (!projects.data?.length) {
      return;
    }

    const storedProjectId = window.localStorage?.getItem("payaffe:selected-project-id") ?? null;
    const nextProjectId = projects.data.some((project) => project.projectId === storedProjectId)
      ? storedProjectId!
      : projects.data.some((project) => project.projectId === selectedProjectId)
        ? selectedProjectId
        : projects.data[0].projectId;
    setSelectedAdminProjectId(nextProjectId);
    setSelectedProjectId(nextProjectId);
  }, [projects.data, selectedProjectId]);

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

  if (projects.isPending) {
    return <CenteredState>{t("loadingProjects")}</CenteredState>;
  }

  if (projects.isError) {
    return (
      <div className="mx-auto w-full max-w-xl px-5 py-12">
        <ErrorMessage error={projects.error} />
      </div>
    );
  }

  const selectedProject = projects.data.find((project) => project.projectId === selectedProjectId);
  if (!selectedProject) {
    return <CenteredState>{t("loadingProjects")}</CenteredState>;
  }

  setSelectedAdminProjectId(selectedProject.projectId);

  const selectProject = (projectId: string) => {
    setSelectedAdminProjectId(projectId);
    window.localStorage?.setItem("payaffe:selected-project-id", projectId);
    setSelectedProjectId(projectId);
  };

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
        onSelectProject={selectProject}
        projects={projects.data}
        selectedProject={selectedProject}
        session={session.data}
      />
      <div className="lg:grid lg:grid-cols-[16rem_minmax(0,1fr)] lg:items-start">
        <AdminNav onNavigate={() => setNavOpen(false)} open={navOpen} />
        <main className="min-w-0 px-5 py-8 sm:px-8" id="admin-main" key={selectedProject.projectId}>
          {children}
        </main>
      </div>
    </div>
  );
}

function TopBar({
  navOpen,
  onToggleNav,
  onSelectProject,
  projects,
  selectedProject,
  session
}: {
  navOpen: boolean;
  onToggleNav: () => void;
  onSelectProject: (projectId: string) => void;
  projects: AdminProject[];
  selectedProject: AdminProject;
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
      <label className="ml-auto flex min-w-0 items-center gap-2 text-sm">
        <span className="hidden text-[var(--muted-foreground)] sm:inline">{t("project")}</span>
        <select
          aria-label={t("project")}
          className="max-w-52 rounded-md border border-[var(--border)] bg-[var(--background)] px-2 py-1.5"
          onChange={(event) => onSelectProject(event.target.value)}
          value={selectedProject.projectId}
        >
          {projects.map((project) => (
            <option key={project.projectId} value={project.projectId}>
              {project.name} ({project.status})
            </option>
          ))}
        </select>
      </label>
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
