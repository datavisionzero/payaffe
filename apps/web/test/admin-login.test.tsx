import { fireEvent, screen } from "@testing-library/react";
import axe from "axe-core";
import { HttpResponse, http } from "msw";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminLoginPage } from "../components/admin/login-page";
import { adminServer, renderAdmin, resetAdminState, session, state } from "./admin-harness";

const nav = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn() }));
vi.mock("../lib/navigation", () => ({
  useRouter: () => ({
    back: vi.fn(),
    forward: vi.fn(),
    prefetch: vi.fn(),
    push: nav.push,
    refresh: vi.fn(),
    replace: nav.replace
  }),
  usePathname: () => "/admin/login",
  useSearchParams: () => new URLSearchParams()
}));

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  nav.replace.mockClear();
  nav.push.mockClear();
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminLoginPage", () => {
  it("completes the MFA sign-in flow and leaves for the admin surface", async () => {
    renderAdmin(<AdminLoginPage />);

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "correct-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(
      await screen.findByRole("heading", { name: "Multi-factor authentication" })
    ).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Authentication code"), {
      target: { value: "123456" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    await vi.waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/admin"));
  });

  it("signs in without a second step when the account has no second factor", async () => {
    // What an installation looks like right after `bootstrap-admin`: a password
    // and nothing else (ADR 0028). The MFA form must not appear at all.
    adminServer.use(
      http.post("/api/admin/auth/login", () => {
        state.authenticated = true;
        return HttpResponse.json({ status: "authenticated", challengeId: null });
      })
    );

    renderAdmin(<AdminLoginPage />);

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "correct-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    await vi.waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/admin"));
    expect(
      screen.queryByRole("heading", { name: "Multi-factor authentication" })
    ).not.toBeInTheDocument();
  });

  it("can complete MFA with a recovery code", async () => {
    renderAdmin(<AdminLoginPage />);

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "correct-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(
      await screen.findByRole("heading", { name: "Multi-factor authentication" })
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Use recovery code" }));
    fireEvent.change(screen.getByLabelText("Recovery code"), {
      target: { value: "ABCD-EFGH-JK23" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    await vi.waitFor(() => {
      expect(state.mfaRequestBody).toEqual({
        challengeId: "f53cb01d-5913-4af5-80f9-09f55aa85b30",
        recoveryCode: "ABCD-EFGH-JK23",
        totpCode: null
      });
    });
    await vi.waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/admin"));
  });

  it("shows a safe message when the credentials are rejected", async () => {
    renderAdmin(<AdminLoginPage />);

    expect(await screen.findByRole("heading", { name: "Admin sign-in" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Username"), {
      target: { value: "admin@example.test" }
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "wrong-password" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The credentials are invalid.");
    expect(nav.replace).not.toHaveBeenCalled();
  });

  it("sends an admin who is already signed in to the admin surface", async () => {
    state.authenticated = true;
    renderAdmin(<AdminLoginPage />);

    await vi.waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/admin"));
    expect(session.username).toBe("admin@example.test");
  });

  it("has no automated accessibility violations", async () => {
    const rendered = renderAdmin(<AdminLoginPage />);
    await screen.findByRole("heading", { name: "Admin sign-in" });

    const result = await axe.run(rendered.container, {
      rules: { "color-contrast": { enabled: false } }
    });
    expect(result.violations).toEqual([]);
  });
});
