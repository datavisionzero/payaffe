export function adminProjectPath(projectId: string, suffix = ""): string {
  return `/admin/projects/${encodeURIComponent(projectId)}${suffix}`;
}

// Project IDs are UUIDs, so this segment never names a Project.
export const adminProjectCreatePath = "/admin/projects/new";

export function projectIdFromAdminPath(pathname: string): string | null {
  const match = /^\/admin\/projects\/([^/]+)(?:\/|$)/.exec(pathname);
  if (!match || match[1] === "new") {
    return null;
  }

  try {
    return decodeURIComponent(match[1]);
  } catch {
    return null;
  }
}

export function switchAdminProjectPath(pathname: string, projectId: string): string {
  const currentProjectId = projectIdFromAdminPath(pathname);
  if (!currentProjectId) {
    return adminProjectPath(projectId);
  }

  const prefix = adminProjectPath(currentProjectId);
  return adminProjectPath(projectId, pathname.slice(prefix.length));
}
