import { useState } from "react";
import { describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PinInput } from "../src/ui/PinInput";

function Harness({ error, autoFocus = false }: { error?: string; autoFocus?: boolean }) {
  const [value, setValue] = useState("");
  return <>
    <PinInput label="Create PIN" value={value} onChange={setValue} error={error} autoFocus={autoFocus} />
    <button type="button" onClick={() => setValue("")}>Clear PIN</button>
  </>;
}

function cells() {
  return Array.from(screen.getByRole("group", { name: "Create PIN" }).querySelectorAll("input"));
}

describe("shared five-position PIN input", () => {
  it("offers five masked, individually labeled numeric touch targets without PIN text", async () => {
    render(<Harness autoFocus />);
    const inputs = cells();
    expect(inputs).toHaveLength(5);
    expect(inputs[0]).toHaveFocus();
    inputs.forEach((input, index) => {
      expect(input).toHaveAttribute("type", "password");
      expect(input).toHaveAttribute("inputmode", "numeric");
      expect(input).toHaveAttribute("maxlength", "1");
      expect(input).toHaveAccessibleName(`Create PIN, digit ${index + 1} of 5`);
    });
    await userEvent.type(inputs[0], "12345");
    expect(inputs.map(input => input.value)).toEqual(["1", "2", "3", "4", "5"]);
    expect(screen.getByRole("group", { name: "Create PIN" }).textContent).not.toContain("12345");
    expect(inputs[4]).toHaveFocus();
  });

  it("keeps each digit in a predictable keyboard tab order", async () => {
    render(<Harness />);
    const user = userEvent.setup();
    for (const input of cells()) {
      await user.tab();
      expect(input).toHaveFocus();
    }
    await user.tab();
    expect(screen.getByRole("button", { name: "Clear PIN" })).toHaveFocus();
  });

  it("rejects letters, symbols, multi-character input, and a sixth digit", async () => {
    render(<Harness />);
    const inputs = cells();
    const user = userEvent.setup();
    await user.type(inputs[0], "a#123456");
    expect(inputs.map(input => input.value)).toEqual(["1", "2", "3", "4", "5"]);
    fireEvent.change(inputs[4], { target: { value: "89" } });
    expect(inputs[4]).toHaveValue("5");
  });

  it("moves backward on Backspace, supports arrow navigation and tapping a position", async () => {
    render(<Harness />);
    const inputs = cells();
    const user = userEvent.setup();
    await user.type(inputs[0], "123");
    expect(inputs[3]).toHaveFocus();
    await user.keyboard("{Backspace}");
    expect(inputs[2]).toHaveFocus();
    expect(inputs[2]).toHaveValue("");
    await user.keyboard("{ArrowLeft}");
    expect(inputs[1]).toHaveFocus();
    await user.keyboard("{Backspace}");
    expect(inputs[1]).toHaveValue("");
    await user.click(inputs[4]);
    expect(inputs[4]).toHaveFocus();
    await user.type(inputs[4], "5");
    expect(inputs[4]).toHaveValue("5");
    await user.click(inputs[4]);
    await user.type(inputs[4], "6");
    expect(inputs[4]).toHaveValue("6");
    await user.keyboard("{Home}");
    expect(inputs[0]).toHaveFocus();
    await user.keyboard("{End}");
    expect(inputs[4]).toHaveFocus();
  });

  it("accepts only an exact five-digit paste and keeps invalid paste out", async () => {
    render(<Harness />);
    const inputs = cells();
    fireEvent.paste(inputs[2], { clipboardData: { getData: () => "09123" } });
    expect(inputs.map(input => input.value)).toEqual(["0", "9", "1", "2", "3"]);
    expect(inputs[4]).toHaveFocus();
    await userEvent.click(screen.getByRole("button", { name: "Clear PIN" }));
    for (const invalid of ["1234", "123456", "12a45", "12-45", " 12345 "]) {
      fireEvent.paste(inputs[0], { clipboardData: { getData: () => invalid } });
      expect(inputs.every(input => input.value === "")).toBe(true);
    }
  });

  it("associates the error with every position without relying on color", () => {
    render(<Harness error="PINs don't match. Try again." />);
    cells().forEach(input => {
      expect(input).toHaveAttribute("aria-invalid", "true");
      expect(input).toHaveAccessibleDescription("PINs don't match. Try again.");
    });
  });
});
