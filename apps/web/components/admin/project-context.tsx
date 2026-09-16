import { createContext, useContext } from "react";
import type { AdminProject } from "../../lib/admin-api";

const ProjectContext = createContext<AdminProject | null>(null);

export function AdminProjectProvider({
  children,
  project
}: {
  children: React.ReactNode;
  project: AdminProject;
}) {
  return <ProjectContext.Provider value={project}>{children}</ProjectContext.Provider>;
}

export function useAdminProject(): AdminProject {
  const project = useContext(ProjectContext);
  if (!project) {
    throw new Error("useAdminProject must be used on a Project route");
  }
  return project;
}
