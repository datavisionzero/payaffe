import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { Navigate } from "react-router";
import { useSearchParams } from "../../lib/navigation";
import Link from "../../lib/link";
import {
  type AdminProject,
  type ProjectForm,
  changeAdminProjectStatus,
  createAdminProject,
  projectFormSchema
} from "../../lib/admin-api";
import {
  EmptyMessage,
  ErrorMessage,
  PageHeader,
  Panel,
  StateMessage,
  StatusPill,
  SubmitButton,
  TextField
} from "./common";
import { formatDateTime } from "./format";
import { adminProjectPath } from "./project-routes";
import { adminQueries } from "./queries";

export function AdminLandingPage() {
  const query = useQuery(adminQueries.projects());
  const projects = query.data ?? [];

  if (query.isPending) {
    return <StateMessage>Loading installation overview</StateMessage>;
  }
  if (query.isError) {
    return <ErrorMessage error={query.error} />;
  }
  if (projects.length === 1) {
    return <Navigate replace to={adminProjectPath(projects[0].projectId)} />;
  }

  const active = projects.filter((project) => project.status === "active").length;
  const disabled = projects.filter((project) => project.status === "disabled").length;

  return (
    <div className="space-y-6">
      <PageHeader
        description="Installation-wide status. Choose a Project to inspect payment operations."
        title="Installation overview"
      />
      <div className="grid gap-4 sm:grid-cols-3">
        <Summary label="Projects" value={String(projects.length)} />
        <Summary label="Active" value={String(active)} />
        <Summary label="Disabled or archived" value={String(projects.length - active)} />
      </div>
      <Panel>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Projects</h2>
            <p className="mt-1 text-sm text-[var(--muted-foreground)]">
              {disabled > 0
                ? `${disabled} disabled Project${disabled === 1 ? "" : "s"} can still process existing work.`
                : "Each Project is an isolated payment-processing boundary."}
            </p>
          </div>
          <Link className="text-sm font-medium text-[var(--brand-ink)]" href="/admin/projects">
            Manage Projects
          </Link>
        </div>
        {projects.length === 0 ? (
          <EmptyMessage>Create a Project before configuring payment operations.</EmptyMessage>
        ) : (
          <ul className="mt-5 grid gap-2">
            {projects.map((project) => (
              <li key={project.projectId}>
                <Link
                  className="flex items-center justify-between gap-3 rounded-md border border-[var(--border)] px-3 py-3 hover:bg-[var(--muted)]"
                  href={adminProjectPath(project.projectId)}
                >
                  <span>
                    <span className="block font-medium">{project.name}</span>
                    <span className="block text-xs text-[var(--muted-foreground)]">{project.slug}</span>
                  </span>
                  <StatusPill status={project.status} />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

function Summary({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--card)] p-4">
      <p className="text-xs font-medium text-[var(--muted-foreground)] uppercase">{label}</p>
      <p className="mt-2 text-2xl font-semibold">{value}</p>
    </div>
  );
}

export function AdminProjectsPage() {
  const queryClient = useQueryClient();
  const query = useQuery(adminQueries.projects());
  const form = useForm<ProjectForm>({ defaultValues: { name: "", slug: "" } });
  const createMutation = useMutation({
    mutationFn: createAdminProject,
    onSuccess: async () => {
      form.reset();
      await queryClient.invalidateQueries({ queryKey: adminQueries.projects().queryKey });
    }
  });
  const statusMutation = useMutation({
    mutationFn: ({ project, status }: { project: AdminProject; status: string }) =>
      changeAdminProjectStatus(project, status),
    onSuccess: async () =>
      queryClient.invalidateQueries({ queryKey: adminQueries.projects().queryKey })
  });
  const submit = form.handleSubmit((values) => {
    const parsed = projectFormSchema.safeParse(values);
    if (!parsed.success) {
      form.setError("name", { message: "Enter a Project name." });
      form.setError("slug", { message: "Use lowercase letters, numbers, and single hyphens." });
      return;
    }
    createMutation.mutate(parsed.data);
  });

  const changeStatus = (project: AdminProject, status: string) => {
    if (
      status === "archived" &&
      !window.confirm(`Archive ${project.name}? The Project becomes read-only.`)
    ) {
      return;
    }
    statusMutation.mutate({ project, status });
  };

  return (
    <div className="space-y-6">
      <PageHeader
        description="Installation-wide Project lifecycle. All Admins can manage every Project."
        title="Projects"
      />
      <Panel>
        <h2 className="text-lg font-semibold">Create Project</h2>
        <form className="mt-4 grid gap-4 sm:grid-cols-2" onSubmit={submit}>
          <TextField
            error={form.formState.errors.name?.message}
            label="Name"
            maxLength={255}
            {...form.register("name")}
          />
          <TextField
            error={form.formState.errors.slug?.message}
            label="Slug"
            maxLength={100}
            {...form.register("slug")}
          />
          <div className="sm:col-span-2">
            <SubmitButton busy={createMutation.isPending}>
              {createMutation.isPending ? "Creating" : "Create Project"}
            </SubmitButton>
          </div>
        </form>
        {createMutation.isError ? <ErrorMessage error={createMutation.error} /> : null}
      </Panel>

      {query.isPending ? <StateMessage>Loading Projects</StateMessage> : null}
      {query.isError ? <ErrorMessage error={query.error} /> : null}
      {statusMutation.isError ? <ErrorMessage error={statusMutation.error} /> : null}
      {query.data?.length === 0 ? <EmptyMessage>No Projects exist yet.</EmptyMessage> : null}
      <div className="grid gap-3">
        {query.data?.map((project) => (
          <article
            className="rounded-md border border-[var(--border)] bg-[var(--card)] p-4"
            key={project.projectId}
          >
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <Link
                  className="font-semibold text-[var(--brand-ink)]"
                  href={adminProjectPath(project.projectId)}
                >
                  {project.name}
                </Link>
                <p className="mt-1 text-sm text-[var(--muted-foreground)]">
                  {project.slug} · Updated {formatDateTime(project.updatedAt)}
                </p>
              </div>
              <StatusPill status={project.status} />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {project.status === "active" ? (
                <StatusButton
                  busy={statusMutation.isPending}
                  onClick={() => changeStatus(project, "disabled")}
                >
                  Disable
                </StatusButton>
              ) : null}
              {project.status === "disabled" ? (
                <>
                  <StatusButton
                    busy={statusMutation.isPending}
                    onClick={() => changeStatus(project, "active")}
                  >
                    Re-enable
                  </StatusButton>
                  <StatusButton
                    busy={statusMutation.isPending}
                    onClick={() => changeStatus(project, "archived")}
                  >
                    Archive
                  </StatusButton>
                </>
              ) : null}
              {project.status === "archived" ? (
                <StatusButton
                  busy={statusMutation.isPending}
                  onClick={() => changeStatus(project, "disabled")}
                >
                  Restore as disabled
                </StatusButton>
              ) : null}
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}

function StatusButton({
  busy,
  children,
  onClick
}: {
  busy: boolean;
  children: React.ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      className="rounded-md border border-[var(--border)] px-3 py-2 text-sm font-medium hover:bg-[var(--muted)] disabled:opacity-60"
      disabled={busy}
      onClick={onClick}
      type="button"
    >
      {children}
    </button>
  );
}

export function LegacyProjectRedirect({ suffix }: { suffix: string }) {
  const query = useQuery(adminQueries.projects());
  const searchParams = useSearchParams();
  if (query.isPending) {
    return <StateMessage>Finding the Project for this legacy address</StateMessage>;
  }
  if (query.isError) {
    return <ErrorMessage error={query.error} />;
  }
  if (query.data.length === 1) {
    const search = searchParams.toString();
    return (
      <Navigate
        replace
        to={`${adminProjectPath(query.data[0].projectId, suffix)}${search ? `?${search}` : ""}`}
      />
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        description="This legacy address does not identify a Project."
        title="Choose a Project"
      />
      <Panel>
        <p className="text-sm text-[var(--muted-foreground)]">
          Select a Project explicitly before opening this view.
        </p>
        <Link className="mt-4 inline-block font-medium text-[var(--brand-ink)]" href="/admin/projects">
          View Projects
        </Link>
      </Panel>
    </div>
  );
}
