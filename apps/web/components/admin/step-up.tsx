"use client";

import { Dialog } from "@base-ui/react/dialog";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useMemo, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { AdminApiError, type AdminMfaForm, adminMfaFormSchema, stepUpAdmin } from "../../lib/admin-api";
import { createText } from "../../lib/text";
import { ErrorMessage, SubmitButton, TextField } from "./common";
import { adminQueries } from "./queries";

const t = createText({
  cancel: "Cancel",
  confirm: "Confirm",
  description:
    "This action needs a recent step-up. Enter the six-digit code from your authenticator app to continue.",
  submitting: "Working",
  title: "Confirm step-up",
  totpCode: "Authentication code",
  "validation.totpCode": "Enter a six-digit authentication code."
});

type StepUpContextValue = {
  /** Asks for the second factor; resolves true once step-up succeeded, false if cancelled. */
  requestStepUp: () => Promise<boolean>;
};

const StepUpContext = createContext<StepUpContextValue | null>(null);

export function isStepUpRequired(error: unknown): boolean {
  return error instanceof AdminApiError && error.code === "admin_step_up.required";
}

export function useStepUp(): StepUpContextValue | null {
  return useContext(StepUpContext);
}

/**
 * Runs a protected action and, when the backend answers that step-up is
 * required, asks for the authentication code and runs the action once more.
 * Cancelling the prompt surfaces the original error.
 */
export function useWithStepUp() {
  const stepUp = useContext(StepUpContext);
  return useCallback(
    async <T,>(action: () => Promise<T>): Promise<T> => {
      try {
        return await action();
      } catch (error) {
        if (!stepUp || !isStepUpRequired(error) || !(await stepUp.requestStepUp())) {
          throw error;
        }
        return action();
      }
    },
    [stepUp]
  );
}

/** For a read that needs step-up: prompts, then lets the caller load again. */
export function StepUpButton({ onConfirmed }: { onConfirmed: () => void }) {
  const stepUp = useContext(StepUpContext);
  if (!stepUp) {
    return null;
  }

  return (
    <button
      className="mt-3 rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--brand)]"
      onClick={() => {
        void stepUp.requestStepUp().then((confirmed) => {
          if (confirmed) {
            onConfirmed();
          }
        });
      }}
      type="button"
    >
      {t("title")}
    </button>
  );
}

export function StepUpProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  // Actions that hit step-up while the prompt is open wait for the same answer.
  const pending = useRef<{ promise: Promise<boolean>; resolve: (value: boolean) => void } | null>(null);
  const form = useForm<AdminMfaForm>({ defaultValues: { totpCode: "" } });
  const mutation = useMutation({
    mutationFn: stepUpAdmin,
    gcTime: 0,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: adminQueries.session().queryKey });
      finish(true);
    }
  });

  const finish = (value: boolean) => {
    pending.current?.resolve(value);
    pending.current = null;
    form.reset({ totpCode: "" });
    mutation.reset();
    setOpen(false);
  };

  const requestStepUp = useCallback(() => {
    if (!pending.current) {
      let resolve: (value: boolean) => void = () => undefined;
      const promise = new Promise<boolean>((done) => {
        resolve = done;
      });
      pending.current = { promise, resolve };
      setOpen(true);
    }
    return pending.current.promise;
  }, []);
  const value = useMemo(() => ({ requestStepUp }), [requestStepUp]);

  const submit = form.handleSubmit((values) => {
    const parsed = adminMfaFormSchema.safeParse(values);
    if (!parsed.success) {
      form.setError("totpCode", { message: t("validation.totpCode") });
      return;
    }
    mutation.mutate(parsed.data);
  });

  return (
    <StepUpContext.Provider value={value}>
      {children}
      <Dialog.Root
        onOpenChange={(next) => {
          if (!next) {
            finish(false);
          }
        }}
        open={open}
      >
        <Dialog.Portal>
          <Dialog.Backdrop className="fixed inset-0 z-40 bg-black/40" />
          <Dialog.Popup className="fixed top-1/2 left-1/2 z-50 w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-md border border-[var(--border)] bg-[var(--card)] p-5 shadow-lg">
            <Dialog.Title className="text-lg font-semibold">{t("title")}</Dialog.Title>
            <Dialog.Description className="mt-2 text-sm text-[var(--muted-foreground)]">
              {t("description")}
            </Dialog.Description>
            <form className="mt-5 grid gap-3" onSubmit={submit}>
              <TextField
                autoComplete="one-time-code"
                autoFocus
                error={form.formState.errors.totpCode?.message}
                inputMode="numeric"
                label={t("totpCode")}
                maxLength={6}
                pattern="[0-9]{6}"
                {...form.register("totpCode")}
              />
              <div className="flex flex-wrap gap-3">
                <SubmitButton busy={mutation.isPending}>
                  {mutation.isPending ? t("submitting") : t("confirm")}
                </SubmitButton>
                <Dialog.Close className="rounded-md border border-[var(--border)] px-4 py-2 text-sm font-medium hover:border-[var(--brand)]">
                  {t("cancel")}
                </Dialog.Close>
              </div>
              {mutation.isError ? <ErrorMessage error={mutation.error} /> : null}
            </form>
          </Dialog.Popup>
        </Dialog.Portal>
      </Dialog.Root>
    </StepUpContext.Provider>
  );
}
