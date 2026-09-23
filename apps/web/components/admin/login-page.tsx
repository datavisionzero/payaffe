"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "../../lib/navigation";
import { createText } from "../../lib/text";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import {
  type AdminLoginForm,
  type AdminMfaCompleteCommand,
  type AdminMfaForm,
  type AdminRecoveryCodeForm,
  adminLoginFormSchema,
  adminMfaFormSchema,
  adminRecoveryCodeFormSchema,
  completeAdminMfa,
  startAdminLogin
} from "../../lib/admin-api";
import { ErrorMessage, SubmitButton, TextField } from "./common";
import { adminQueries } from "./queries";
import { ThemeSelect } from "../theme-select";

const t = createText({
  back: "Back",
  continue: "Continue",
  loginDescription: "Use your local Admin Account.",
  loginTitle: "Admin sign-in",
  mfaDescription: "Enter the six-digit code from your authenticator app.",
  mfaTitle: "Multi-factor authentication",
  password: "Password",
  recoveryCode: "Recovery code",
  signIn: "Sign in",
  submitting: "Working",
  totpCode: "Authentication code",
  useAuthenticatorCode: "Use authenticator code",
  useRecoveryCode: "Use recovery code",
  username: "Username",
  "validation.loginRequired": "Username and password are required.",
  "validation.recoveryCode": "Enter a recovery code.",
  "validation.totpCode": "Enter a six-digit authentication code."
});

export function AdminLoginPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const session = useQuery(adminQueries.session());
  const [challengeId, setChallengeId] = useState<string | null>(null);
  const [mfaMode, setMfaMode] = useState<"totp" | "recovery">("totp");
  const loginForm = useForm<AdminLoginForm>({
    defaultValues: {
      username: "",
      password: ""
    }
  });
  const mfaForm = useForm<AdminMfaForm>({
    defaultValues: {
      totpCode: ""
    }
  });
  const recoveryCodeForm = useForm<AdminRecoveryCodeForm>({
    defaultValues: {
      recoveryCode: ""
    }
  });
  const loginMutation = useMutation({
    mutationFn: startAdminLogin,
    onSuccess: async (result) => {
      loginForm.reset({ username: loginForm.getValues("username"), password: "" });

      // An account with no second factor is signed in already; the response
      // carried the session cookie and there is no second step to show
      // (ADR 0028). Refreshing the session query is what moves the UI on.
      if (result.status === "authenticated") {
        setChallengeId(null);
        await queryClient.invalidateQueries({ queryKey: adminQueries.session().queryKey });
        return;
      }

      setChallengeId(result.challengeId);
      setMfaMode("totp");
      mfaForm.reset({ totpCode: "" });
      recoveryCodeForm.reset({ recoveryCode: "" });
    }
  });
  const mfaMutation = useMutation({
    mutationFn: (command: AdminMfaCompleteCommand) => completeAdminMfa(challengeId!, command),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: adminQueries.session().queryKey });
    }
  });

  // Both the completed sign-in and an admin who was already signed in arrive
  // here the same way: the session query has data and this route has nothing
  // left to ask for.
  const authenticated = Boolean(session.data);
  useEffect(() => {
    if (authenticated) {
      router.replace("/admin");
    }
  }, [authenticated, router]);

  const submitLogin = loginForm.handleSubmit((values) => {
    const parsed = adminLoginFormSchema.safeParse(values);
    if (!parsed.success) {
      loginForm.setError("username", { message: t("validation.loginRequired") });
      return;
    }

    loginMutation.mutate(parsed.data);
  });

  const submitMfa = mfaForm.handleSubmit((values) => {
    const parsed = adminMfaFormSchema.safeParse(values);
    if (!parsed.success) {
      mfaForm.setError("totpCode", { message: t("validation.totpCode") });
      return;
    }

    mfaMutation.mutate(parsed.data);
  });
  const submitRecoveryCode = recoveryCodeForm.handleSubmit((values) => {
    const parsed = adminRecoveryCodeFormSchema.safeParse(values);
    if (!parsed.success) {
      recoveryCodeForm.setError("recoveryCode", { message: t("validation.recoveryCode") });
      return;
    }

    mfaMutation.mutate(parsed.data);
  });

  return (
    <main className="mx-auto flex min-h-screen w-full max-w-xl flex-col justify-center px-5 py-12">
      <div className="flex items-center justify-between gap-4">
        <p className="flex items-center gap-2 text-sm font-semibold">
          <span aria-hidden="true" className="size-4 rounded-sm bg-[var(--brand)]" />
          payaffe
        </p>
        <ThemeSelect compact />
      </div>
      <section className="mt-4 rounded-md border border-[var(--border)] bg-[var(--card)] p-5 shadow-sm">
        <h1 className="text-xl font-semibold">{challengeId ? t("mfaTitle") : t("loginTitle")}</h1>
        <p className="mt-2 text-sm text-[var(--muted-foreground)]">
          {challengeId ? t("mfaDescription") : t("loginDescription")}
        </p>

        {!challengeId ? (
          <form className="mt-6 space-y-4" onSubmit={submitLogin}>
            <TextField
              autoComplete="username"
              error={loginForm.formState.errors.username?.message}
              label={t("username")}
              type="text"
              {...loginForm.register("username")}
            />
            <TextField
              autoComplete="current-password"
              error={loginForm.formState.errors.password?.message}
              label={t("password")}
              type="password"
              {...loginForm.register("password")}
            />
            <SubmitButton busy={loginMutation.isPending}>
              {loginMutation.isPending ? t("submitting") : t("continue")}
            </SubmitButton>
            {loginMutation.isError ? <ErrorMessage error={loginMutation.error} /> : null}
          </form>
        ) : mfaMode === "totp" ? (
          <form className="mt-6 space-y-4" onSubmit={submitMfa}>
            <TextField
              autoComplete="one-time-code"
              error={mfaForm.formState.errors.totpCode?.message}
              inputMode="numeric"
              label={t("totpCode")}
              maxLength={6}
              pattern="[0-9]{6}"
              {...mfaForm.register("totpCode")}
            />
            <div className="flex flex-wrap gap-3">
              <SubmitButton busy={mfaMutation.isPending}>
                {mfaMutation.isPending ? t("submitting") : t("signIn")}
              </SubmitButton>
              <button
                className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--brand)]"
                onClick={() => {
                  setChallengeId(null);
                  mfaForm.reset({ totpCode: "" });
                  recoveryCodeForm.reset({ recoveryCode: "" });
                }}
                type="button"
              >
                {t("back")}
              </button>
              <button
                className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--brand)]"
                onClick={() => {
                  setMfaMode("recovery");
                  mfaForm.reset({ totpCode: "" });
                }}
                type="button"
              >
                {t("useRecoveryCode")}
              </button>
            </div>
            {mfaMutation.isError ? <ErrorMessage error={mfaMutation.error} /> : null}
          </form>
        ) : (
          <form className="mt-6 space-y-4" onSubmit={submitRecoveryCode}>
            <TextField
              autoComplete="one-time-code"
              error={recoveryCodeForm.formState.errors.recoveryCode?.message}
              label={t("recoveryCode")}
              maxLength={64}
              {...recoveryCodeForm.register("recoveryCode")}
            />
            <div className="flex flex-wrap gap-3">
              <SubmitButton busy={mfaMutation.isPending}>
                {mfaMutation.isPending ? t("submitting") : t("signIn")}
              </SubmitButton>
              <button
                className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--brand)]"
                onClick={() => {
                  setMfaMode("totp");
                  recoveryCodeForm.reset({ recoveryCode: "" });
                }}
                type="button"
              >
                {t("useAuthenticatorCode")}
              </button>
              <button
                className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--brand)]"
                onClick={() => {
                  setChallengeId(null);
                  mfaForm.reset({ totpCode: "" });
                  recoveryCodeForm.reset({ recoveryCode: "" });
                }}
                type="button"
              >
                {t("back")}
              </button>
            </div>
            {mfaMutation.isError ? <ErrorMessage error={mfaMutation.error} /> : null}
          </form>
        )}
      </section>
    </main>
  );
}
