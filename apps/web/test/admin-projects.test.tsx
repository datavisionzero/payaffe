import { fireEvent, screen } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { AdminProjectsPage } from "../components/admin/projects-page";
import { adminServer, renderAdmin, resetAdminState, state } from "./admin-harness";

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminProjectsPage", () => {
  it("creates a Project with CSRF and makes it available for navigation", async () => {
    state.authenticated = true;
    renderAdmin(<AdminProjectsPage />);

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Storefront" } });
    fireEvent.change(screen.getByLabelText("Slug"), { target: { value: "storefront" } });
    fireEvent.click(screen.getByRole("button", { name: "Create Project" }));

    expect(await screen.findByRole("link", { name: "Storefront" })).toHaveAttribute(
      "href",
      "/admin/projects/2a683f48-acde-4b22-bc3f-468d283296af"
    );
    expect(state.csrf.createProject).toBe("csrf-token");
  });

  it("supports disable, re-enable, archive, and restore transitions", async () => {
    state.authenticated = true;
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderAdmin(<AdminProjectsPage />);

    fireEvent.click(await screen.findByRole("button", { name: "Disable" }));
    expect(await screen.findByRole("button", { name: "Re-enable" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Re-enable" }));
    expect(await screen.findByRole("button", { name: "Disable" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Disable" }));
    fireEvent.click(await screen.findByRole("button", { name: "Archive" }));
    expect(await screen.findByRole("button", { name: "Restore as disabled" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Restore as disabled" }));
    expect(await screen.findByRole("button", { name: "Re-enable" })).toBeInTheDocument();
    expect(state.csrf.changeProjectStatus).toBe("csrf-token");
  });
});
