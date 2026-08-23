import createClient from "openapi-fetch";
import type { components, paths } from "./generated";

export type ApiProblem = components["schemas"]["IntegrationApiProblemResponse"];

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
    public readonly correlationId?: string,
    public readonly fieldErrors: Record<string, string[]> = {},
    public readonly retryAfter?: string
  ) {
    super(message);
    this.name = "ApiError";
  }
}

// Where the API is. Every route this client calls lives under /api, so the
// ordinary answer is "the origin this page came from": a reverse proxy sends
// /api to the API host and everything else here, and one built bundle then
// works at any address. That is what lets a published web image exist at all,
// because Next.js compiles NEXT_PUBLIC_* into the bundle and a compiled-in
// address would pin the image to one installation.
//
// The variable is for the other topology, where the API answers somewhere else
// entirely and the browser calls it cross-origin. Empty counts as unset: a
// Docker build argument that was never passed still arrives as an empty
// string, and it means the same thing.
function resolveApiBaseUrl(): string {
  const configured = process.env.NEXT_PUBLIC_PAYAFFE_API_BASE_URL;
  if (configured !== undefined && configured !== "") {
    return configured;
  }

  // No request and no origin during prerender. Nothing is fetched then; this
  // only keeps the client constructible at module scope.
  return typeof window === "undefined" ? "http://localhost" : window.location.origin;
}

export const webApi = createClient<paths>({
  baseUrl: resolveApiBaseUrl(),
  fetch: (request) => globalThis.fetch(request),
  credentials: "include",
  headers: {
    Accept: "application/json"
  }
});

export function throwApiError(response: Response, problem: unknown): never {
  const value = isApiProblem(problem) ? problem : undefined;
  if (
    value?.code === "admin_session.invalid" &&
    typeof window !== "undefined"
  ) {
    window.dispatchEvent(new Event("payaffe:admin-session-expired"));
  }
  throw new ApiError(
    value?.title ?? "Request failed.",
    response.status,
    value?.code ?? undefined,
    value?.correlationId ?? undefined,
    value?.errors ?? {},
    response.headers.get("Retry-After") ?? undefined
  );
}

export async function getAdminCsrfToken(): Promise<string> {
  const { data, error, response } = await webApi.GET("/api/admin/csrf");
  if (!data) {
    throwApiError(response, error);
  }

  return data.csrfToken;
}

export async function adminMutationHeaders(): Promise<{ "X-CSRF-TOKEN": string }> {
  return { "X-CSRF-TOKEN": await getAdminCsrfToken() };
}

function isApiProblem(value: unknown): value is ApiProblem {
  return typeof value === "object" && value !== null;
}
