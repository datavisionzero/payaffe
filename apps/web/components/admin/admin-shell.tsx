"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "../../lib/link";
import { usePathname, useRouter, useSearchParams } from "../../lib/navigation";
import { createText } from "../../lib/text";
import { useEffect, useRef, useState } from "react";
import { MenuIcon, UserCircleIcon } from "lucide-react";
import { type AdminProject, type AdminSession } from "../../lib/admin-api";
import { ErrorMessage, StateMessage, StatusPill } from "./common";
import { AdminNav } from "./admin-nav";
import { adminQueries } from "./queries";
import { ThemeSelect } from "../theme-select";
import { AdminProjectProvider } from "./project-context";
import { adminProjectPath, projectIdFromAdminPath, switchAdminProjectPath } from "./project-routes";

const t = createText({
  checkingSession: "Checking session",
  loadingProjects: "Loading projects",
  menu: "Menu",
  project: "Project",
  redirecting: "Taking you to sign-in.",
  signedInAs: "Signed in as",
  skipToContent: "Skip to content",
  testModeTitle: "Test mode",
  testModeDescription:
    "Payments in this installation are simulated: addresses, exchange rates and blockchain observation are not real, and no real money is received."
});

export function AdminShell({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const [navOpen, setNavOpen] = useState(false);
  const routeProjectId = projectIdFromAdminPath(pathname);
  const previousProjectId = useRef(routeProjectId);
  const session = useQuery(adminQueries.session());
  const projects = useQuery({
    ...adminQueries.projects(),
    enabled: Boolean(session.data)
  });

  useEffect(() => {
    const previous = previousProjectId.current;
    previousProjectId.current = routeProjectId;
    if (previous && previous !== routeProjectId) {
      void queryClient
        .cancelQueries({ queryKey: ["admin-project", previous] })
        .finally(() => queryClient.removeQueries({ queryKey: ["admin-project", previous] }));
    }
    if (routeProjectId) {
      window.localStorage?.setItem("payaffe:selected-project-id", routeProjectId);
    }
  }, [queryClient, routeProjectId]);

  useEffect(() => {
    if (
      routeProjectId &&
      projects.data &&
      !projects.data.some((project) => project.projectId === routeProjectId)
    ) {
      window.localStorage?.removeItem("payaffe:selected-project-id");
      void queryClient
        .cancelQueries({ queryKey: ["admin-project", routeProjectId] })
        .finally(() => queryClient.removeQueries({ queryKey: ["admin-project", routeProjectId] }));
    }
  }, [projects.data, queryClient, routeProjectId]);

  useEffect(() => {
    const expireSession = () => {
      queryClient.removeQueries({
        predicate: (candidate) => String(candidate.queryKey[0]).startsWith("admin")
      });
      queryClient.setQueryData(adminQueries.session().queryKey, null);
      // One-time secrets live in mutation results; none may outlive the session.
      queryClient.getMutationCache().clear();
      window.localStorage?.removeItem("payaffe:selected-project-id");
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

  const selectedProject = routeProjectId
    ? projects.data.find((project) => project.projectId === routeProjectId) ?? null
    : null;
  const routeProjectMissing = routeProjectId !== null && selectedProject === null;
  // Installation views carry no Project in their route; the navigation and the
  // switcher keep the last chosen one so the Admin does not lose it.
  const rememberedProjectId = routeProjectId ? null : readSelectedProjectId();
  const navigationProject = routeProjectId
    ? selectedProject
    : projects.data.find((project) => project.projectId === rememberedProjectId) ?? null;

  const selectProject = async (projectId: string) => {
    if (!projectId) {
      router.push("/admin/projects");
      return;
    }
    if (routeProjectId && routeProjectId !== projectId) {
      await queryClient.cancelQueries({ queryKey: ["admin-project", routeProjectId] });
      queryClient.removeQueries({ queryKey: ["admin-project", routeProjectId] });
    }
    window.localStorage?.setItem("payaffe:selected-project-id", projectId);
    const nextPath = switchAdminProjectPath(pathname, projectId);
    const query = searchParams.toString();
    router.push(`${nextPath}${query && routeProjectId ? `?${query}` : ""}`);
  };

  return (
    <div className="min-h-screen bg-[var(--background)]">
      <a
        className="sr-only rounded-md bg-[var(--primary)] px-4 py-2 text-sm font-medium text-[var(--primary-foreground)] focus:not-sr-only focus:absolute focus:top-3 focus:left-3 focus:z-40"
        href="#admin-main"
      >
        {t("skipToContent")}
      </a>
      <TopBar
        navOpen={navOpen}
        onToggleNav={() => setNavOpen((open) => !open)}
        onSelectProject={(projectId) => void selectProject(projectId)}
        projects={projects.data}
        selectedProject={navigationProject}
        session={session.data}
      />
      {session.data.testMode ? <TestModeBanner /> : null}
      {navOpen ? (
        <button
          aria-label="Close navigation"
          className="fixed inset-0 top-12 z-20 bg-[color:color-mix(in_oklch,var(--foreground)_30%,transparent)] lg:hidden"
          onClick={() => setNavOpen(false)}
          type="button"
        />
      ) : null}
      <div className="lg:grid lg:grid-cols-[15rem_minmax(0,1fr)] lg:items-start">
        <AdminNav
          onNavigate={() => setNavOpen(false)}
          open={navOpen}
          project={navigationProject}
        />
        <main
          className="min-w-0 px-4 py-6 sm:px-6 lg:px-8 lg:py-8"
          id="admin-main"
          key={selectedProject?.projectId ?? "installation"}
        >
          {routeProjectMissing ? (
            <div className="mx-auto max-w-xl py-8">
              <StateMessage>
                This Project is unavailable. It may have been removed or your access may have changed.{" "}
                <Link className="font-medium text-[var(--brand-ink)]" href="/admin/projects">
                  Choose a Project
                </Link>
              </StateMessage>
            </div>
          ) : selectedProject ? (
            <AdminProjectProvider project={selectedProject}>
              <ProjectContextBanner project={selectedProject} />
              {selectedProject.status === "archived" ? (
                <fieldset className="m-0 min-w-0 border-0 p-0" disabled>
                  <legend className="sr-only">Archived Project controls are read-only</legend>
                  {children}
                </fieldset>
              ) : (
                children
              )}
            </AdminProjectProvider>
          ) : (
            children
          )}
        </main>
      </div>
    </div>
  );
}

function readSelectedProjectId(): string | null {
  try {
    return window.localStorage?.getItem("payaffe:selected-project-id") ?? null;
  } catch {
    return null;
  }
}

function TestModeBanner() {
  return (
    <section
      aria-labelledby="admin-test-mode-title"
      className="border-b-2 border-dashed border-[var(--danger)] bg-[var(--card)] px-4 py-2 text-sm sm:px-6 lg:px-8"
    >
      <span className="font-semibold uppercase tracking-wide" id="admin-test-mode-title">
        {t("testModeTitle")}
      </span>{" "}
      <span className="text-[var(--muted-foreground)]">{t("testModeDescription")}</span>
    </section>
  );
}

function ProjectContextBanner({ project }: { project: AdminProject }) {
  return (
    <div className="mb-5 flex flex-wrap items-center gap-2 text-xs text-[var(--muted-foreground)]">
      <span>Project</span>
      <Link
        className="font-medium text-[var(--foreground)] hover:text-[var(--brand-ink)]"
        href={adminProjectPath(project.projectId)}
      >
        {project.name}
      </Link>
      <StatusPill status={project.status} />
      {project.status === "disabled" ? (
        <span>New Payments and new integration configuration are disabled.</span>
      ) : null}
      {project.status === "archived" ? <span>Archived Projects are read-only.</span> : null}
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
  selectedProject: AdminProject | null;
  session: AdminSession;
}) {
  return (
    <header className="sticky top-0 z-30 flex h-12 items-center justify-between gap-3 border-b border-[var(--border)] bg-[var(--card)] px-3">
      <div className="flex items-center gap-3">
        <button
          aria-controls="admin-navigation"
          aria-expanded={navOpen}
          className="inline-flex size-8 items-center justify-center rounded-md border border-[var(--border)] hover:bg-[var(--muted)] lg:hidden"
          onClick={onToggleNav}
          type="button"
        >
          <MenuIcon aria-hidden="true" className="size-4" />
          <span className="sr-only">{t("menu")}</span>
        </button>
        <Link className="flex items-center gap-2 text-sm font-semibold" href="/admin">
          <span aria-hidden="true" className="size-4 rounded-sm bg-[var(--brand)]" />
          <span className="hidden sm:inline">payaffe</span>
        </Link>
      </div>
      <label className="ml-auto flex w-28 min-w-0 items-center gap-2 text-xs sm:w-auto">
        <span className="hidden text-[var(--muted-foreground)] sm:inline">{t("project")}</span>
        <select
          aria-label={t("project")}
          className="h-8 w-full min-w-0 rounded-md border border-[var(--border)] bg-[var(--background)] px-2 text-xs sm:max-w-56"
          onChange={(event) => onSelectProject(event.target.value)}
          value={selectedProject?.projectId ?? ""}
        >
          <option value="">Select Project…</option>
          {projects.map((project) => (
            <option key={project.projectId} value={project.projectId}>
              {project.name} ({project.status})
            </option>
          ))}
        </select>
      </label>
      <ThemeSelect compact />
      <Link
        className="inline-flex size-8 shrink-0 items-center justify-center rounded-md hover:bg-[var(--muted)]"
        href="/admin/account"
        title={session.username}
      >
        <span className="sr-only">{t("signedInAs")} </span>
        <UserCircleIcon aria-hidden="true" className="size-4" />
        <span className="sr-only">{session.username}</span>
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
