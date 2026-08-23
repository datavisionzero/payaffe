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

export const webApi = createClient<paths>({
  baseUrl:
    process.env.NEXT_PUBLIC_PAYAFFE_API_BASE_URL ??
    (typeof window === "undefined" ? "http://localhost" : window.location.origin),
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
  const { data, error, response } = await webApi.GET("/admin/csrf");
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
