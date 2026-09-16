import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { LegalDocumentPage } from "../src/app/LegalDocumentPage";

describe("Pilot legal document pages", () => {
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("renders the concise Pilot/Draft Terms without claiming final review", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        "Weymela Pilot Terms of Service\n\nPilot draft — Version pilot-draft-2026-09-16.1\n\nAccurate information is required.",
        { status: 200 },
      ),
    );
    render(
      <MemoryRouter>
        <LegalDocumentPage kind="terms" />
      </MemoryRouter>,
    );

    expect(screen.getByRole("status")).toHaveTextContent("Opening document");
    expect(await screen.findByText(/Pilot draft — Version/)).toBeVisible();
    expect(screen.getByRole("heading", { name: "Terms of Service" })).toBeVisible();
    expect(fetch).toHaveBeenCalledWith(
      "/legal/pilot-terms-of-service-v1.txt",
      expect.objectContaining({ credentials: "same-origin" }),
    );
    expect(screen.queryByText(/attorney-reviewed final Production terms$/)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back" })).toHaveAttribute("href", "/onboarding");
  });

  it("renders Privacy and offers a keyboard-accessible retry on failure", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(new Response("Unavailable", { status: 503 }))
      .mockResolvedValueOnce(
        new Response("Weymela Pilot Privacy Policy\n\nPilot draft privacy content.", {
          status: 200,
        }),
      );
    render(
      <MemoryRouter>
        <LegalDocumentPage kind="privacy" />
      </MemoryRouter>,
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "We couldn't open this document.",
    );
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/Pilot draft privacy content/)).toBeVisible();
  });
});
