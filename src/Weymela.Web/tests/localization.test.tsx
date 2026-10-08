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
    ["M-PESA", "M-PESA"],
    ["Promoted by:", "ያስተዋወቀው፦"],
    ["Full Account / Phone", "ሙሉ የሂሳብ / ስልክ ቁጥር"],
    ["Create Account", "መለያ ፍጠር"],
    ["Customer", "ሸማች"],
    ["Business Owner", "ንግድ ባለቤት"],
    ["Content Creator", "ይዘት ፈጣሪ"],
    ["Pending Review", "በግምገማ ላይ"],
    ["Full legal name", "ሙሉ ሕጋዊ ስም"],
    ["Verify", "አረጋግጥ"],
    ["Create your PIN", "PINዎን ይፍጠሩ"],
    ["Enter your five-digit PIN.", "ባለአምስት አሃዝ PINዎን ያስገቡ።"],
    ["The code is invalid or expired.", "ኮዱ ትክክል አይደለም ወይም ጊዜው አልፏል።"],
    ["New Business awaiting review", "አዲስ ንግድ ግምገማ እየጠበቀ ነው"],
    ["Creator profile approved", "የፈጣሪ መገለጫው ጸድቋል"],
    ["This phone number cannot be added to your account.", "ይህን ስልክ ቁጥር ወደ መለያዎ ማከል አይቻልም።"],
  ])("uses reviewed Amharic for %s", (english, amharic) => {
    expect(translateText(english, "am")).toBe(amharic);
  });

  it("translates signup status, review counts, and notifications without changing user data", () => {
    expect(translateText("Pending Review (3)", "am")).toBe("በግምገማ ላይ (3)");
    expect(translateText("A Creator application is waiting for review.", "am"))
      .toBe("የፈጣሪ ማመልከቻ ግምገማ እየጠበቀ ነው።");
    expect(translateText("Your Business profile was not approved: Missing license", "am"))
      .toBe("የንግድ መገለጫዎ አልጸደቀም፦ Missing license");
    expect(translateText("Followers: 12,500", "am")).toBe("ተከታዮች፦ 12,500");
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
