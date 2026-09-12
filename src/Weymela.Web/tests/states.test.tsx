import { describe, expect, it, vi, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ApiError, useAction, useResource } from "../src/api/client";
import { Button, Field, Resource, Tabs } from "../src/ui/components";
import { BusinessCampaigns } from "../src/features/business/BusinessPages";
import { mockApi } from "./fixtures";
afterEach(() => vi.unstubAllGlobals());
describe("Shared designed states and accessibility", () => {
  it("shows a labeled loading state", () => {
    render(
      <MemoryRouter>
        <Resource
          resource={{ data: null, loading: true, error: null, reload: vi.fn() }}
        >
          {() => null}
        </Resource>
      </MemoryRouter>,
    );
    expect(
      screen.getByRole("status", { name: "Loading workspace" }),
    ).toBeVisible();
  });
  it("shows recoverable errors with a retry action", async () => {
    const reload = vi.fn();
    render(
      <MemoryRouter>
        <Resource
          resource={{
            data: null,
            loading: false,
            error: new Error("Temporarily unavailable"),
            reload,
          }}
        >
          {() => null}
        </Resource>
      </MemoryRouter>,
    );
    expect(screen.getByText("Temporarily unavailable")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(reload).toHaveBeenCalledOnce();
  });
  it("offers sign-in for an expired session", () => {
    render(
      <MemoryRouter>
        <Resource
          resource={{
            data: null,
            loading: false,
            error: new ApiError(401, "Sign in to continue."),
            reload: vi.fn(),
          }}
        >
          {() => null}
        </Resource>
      </MemoryRouter>,
    );
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute(
      "href",
      "/sign-in",
    );
  });
  it("has an explicit unauthorized state", () => {
    render(
      <MemoryRouter>
        <Resource
          resource={{
            data: null,
            loading: false,
            error: new ApiError(403, "This workspace is not available."),
            reload: vi.fn(),
          }}
        >
          {() => null}
        </Resource>
      </MemoryRouter>,
    );
    expect(
      screen.getByRole("heading", { name: "Workspace unavailable" }),
    ).toBeVisible();
  });
  it("offers Campaign creation in an empty Business list", async () => {
    mockApi({ "/business/campaigns": [] });
    render(
      <MemoryRouter>
        <BusinessCampaigns />
      </MemoryRouter>,
    );
    expect(await screen.findByText("No Campaigns to show")).toBeVisible();
    expect(
      screen.getAllByRole("link", { name: "Create Campaign" }).length,
    ).toBeGreaterThan(0);
  });
  it("separates a concise input label from its help description", () => {
    render(
      <Field
        label="Creator Budget"
        help="This protects funds for this Creator."
      >
        <input />
      </Field>,
    );
    const input = screen.getByLabelText("Creator Budget");
    expect(input).toHaveAccessibleName("Creator Budget");
    expect(input).toHaveAccessibleDescription(
      "This protects funds for this Creator.",
    );
  });
  it("does not leave old Campaign data visible when the resource path changes", async () => {
    let respond: ((response: Response) => void) | undefined;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) =>
        url.endsWith("/one")
          ? new Response(JSON.stringify({ name: "First Campaign" }))
          : await new Promise<Response>((resolve) => {
              respond = resolve;
            }),
      ),
    );
    function Example({ path }: { path: string }) {
      const r = useResource<{ name: string }>(path);
      return <Resource resource={r}>{(v) => <h1>{v.name}</h1>}</Resource>;
    }
    const view = render(<Example path="/one" />);
    await screen.findByText("First Campaign");
    view.rerender(<Example path="/two" />);
    expect(screen.queryByText("First Campaign")).not.toBeInTheDocument();
    await act(async () =>
      respond!(new Response(JSON.stringify({ name: "Second Campaign" }))),
    );
    expect(await screen.findByText("Second Campaign")).toBeVisible();
  });
  it("does not queue a financial action while offline", async () => {
    const call = vi.fn();
    Object.defineProperty(navigator, "onLine", {
      get: () => false,
      configurable: true,
    });
    function Example() {
      const a = useAction();
      return (
        <>
          <Button onClick={() => void a.run(call)}>Pay</Button>
          <p role="alert">{a.error}</p>
        </>
      );
    }
    render(<Example />);
    await userEvent.click(screen.getByRole("button", { name: "Pay" }));
    expect(call).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Nothing has been queued",
    );
    Object.defineProperty(navigator, "onLine", {
      get: () => true,
      configurable: true,
    });
  });
  it("disables repeated clicks while a financial action is pending", async () => {
    let done: (() => void) | undefined;
    const call = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          done = resolve;
        }),
    );
    function Example() {
      const a = useAction();
      return (
        <Button disabled={a.busy} onClick={() => void a.run(call)}>
          Save
        </Button>
      );
    }
    render(<Example />);
    await userEvent.click(screen.getByRole("button"));
    expect(screen.getByRole("button")).toBeDisabled();
    await userEvent.click(screen.getByRole("button"));
    expect(call).toHaveBeenCalledOnce();
    await act(async () => done!());
    await waitFor(() => expect(screen.getByRole("button")).toBeEnabled());
  });
  it("moves tab focus with Home and End without hover-only controls", async () => {
    const onChange = vi.fn();
    render(
      <Tabs
        label="Sections"
        items={[
          { value: "a", label: "First" },
          { value: "b", label: "Last" },
        ]}
        value="a"
        onChange={onChange}
      />,
    );
    screen.getByRole("tab", { name: "First" }).focus();
    await userEvent.keyboard("{End}");
    expect(onChange).toHaveBeenCalledWith("b");
    expect(screen.getByRole("tab", { name: "Last" })).toHaveFocus();
    await userEvent.keyboard("{Home}");
    expect(screen.getByRole("tab", { name: "First" })).toHaveFocus();
  });
});
