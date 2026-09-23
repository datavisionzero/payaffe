import { fireEvent, screen } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";
import { AdminProjectCreatePage, AdminProjectsPage } from "../components/admin/projects-page";
import { adminServer, renderAdmin, resetAdminState, state } from "./admin-harness";

beforeAll(() => adminServer.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  resetAdminState();
  vi.restoreAllMocks();
  adminServer.resetHandlers();
});
afterAll(() => adminServer.close());

describe("AdminProjectsPage", () => {
  it("lists Projects with a link to the separate create page", async () => {
    state.authenticated = true;
    renderAdmin(<AdminProjectsPage />);

    expect(await screen.findByRole("link", { name: "Default Project" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create Project" })).toHaveAttribute(
      "href",
      "/admin/projects/new"
    );
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();
  });

  it("creates a Project with CSRF and returns to the list for navigation", async () => {
    state.authenticated = true;
    renderAdmin(
      <Routes>
        <Route path="/admin/projects" element={<AdminProjectsPage />} />
        <Route path="/admin/projects/new" element={<AdminProjectCreatePage />} />
      </Routes>,
      { initialEntries: ["/admin/projects/new"] }
    );

    expect(screen.getByRole("link", { name: "Cancel" })).toHaveAttribute("href", "/admin/projects");
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
