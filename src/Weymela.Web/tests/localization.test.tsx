import { afterEach, describe, expect, it } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LanguageChoice, LanguageProvider, translateText } from "../src/localization/Language";

afterEach(() => {
  localStorage.removeItem("weymela.language");
  document.documentElement.lang = "en";
});

describe("English and Amharic localization", () => {
  it.each([
    ["Business workspace", "የንግድ የሥራ ቦታ"],
    ["Creator workspace", "የፈጣሪ የሥራ ቦታ"],
    ["Customer workspace", "የደንበኛ የሥራ ቦታ"],
    ["Cashier workspace", "የገንዘብ ተቀባይ የሥራ ቦታ"],
    ["Platform Admin", "የመድረክ አስተዳዳሪ"],
    ["Operations Admin", "የኦፕሬሽን አስተዳዳሪ"],
  ])("translates the %s role surface", (english, amharic) => {
    expect(translateText(english, "en")).toBe(english);
    expect(translateText(english, "am")).toBe(amharic);
  });

  it.each([
    ["Price", "ዋጋ"],
    ["Promotions", "ማስታወቂያዎች"],
    ["Promotion", "ማስታወቂያ"],
    ["Settings", "ቅንብሮች"],
    ["Help", "እገዛ"],
    ["Contact Us", "ያግኙን"],
    ["Delete Account", "መለያ ሰርዝ"],
    ["Confirm", "አረጋግጥ"],
    ["Cashback", "ተመላሽ ገንዘብ"],
    ["Earnings", "ገቢዎች"],
    ["Business Location", "የንግድ አድራሻ"],
    ["Get Directions", "አቅጣጫ ያግኙ"],
  ])("uses reviewed Amharic for %s", (english, amharic) => {
    expect(translateText(english, "am")).toBe(amharic);
  });

  it("persists Amharic, translates shared controls, and never translates names or amounts", async () => {
    localStorage.setItem("weymela.language", "am");
    render(<LanguageProvider>
      <LanguageChoice />
      <button type="button">Save</button>
      <span data-no-translate>Business</span>
      <span>1,250 ETB</span>
    </LanguageProvider>);

    expect(await screen.findByRole("button", { name: "አስቀምጥ" })).toBeVisible();
    expect(document.documentElement.lang).toBe("am");
    expect(screen.getByText("Business")).toBeVisible();
    expect(screen.getByText("1,250 ETB")).toBeVisible();

    await userEvent.click(screen.getByRole("button", { name: "English" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeVisible());
    expect(localStorage.getItem("weymela.language")).toBe("en");
    expect(document.documentElement.lang).toBe("en");
  });
});
