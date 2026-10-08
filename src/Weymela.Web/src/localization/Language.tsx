import {
  createContext,
  useContext,
  useEffect,
  useLayoutEffect,
  useState,
  type ReactNode,
} from "react";

export type Language = "en" | "am";

const storageKey = "weymela.language";
const LanguageContext = createContext<{
  language: Language;
  setLanguage: (language: Language) => void;
}>({ language: "en", setLanguage: () => {} });

const amharic: Record<string, string> = {
  "Home": "መነሻ",
  "Dashboard": "ዳሽቦርድ",
  "Discover": "ያግኙ",
  "Promotions": "ማስተዋወቂያዎች",
  "Campaigns": "ዘመቻዎች",
  "Requests": "ጥያቄዎች",
  "Wallet": "የገንዘብ ቦርሳ",
  "Wallets": "የገንዘብ ቦርሳዎች",
  "Transactions": "ግብይቶች",
  "Checkout": "ክፍያ ማረጋገጫ",
  "Purchase": "ግዢ",
  "Profile": "መገለጫ",
  "Earnings": "ገቢ",
  "Customers": "ደንበኞች",
  "Creators": "ፈጣሪዎች",
  "Businesses": "ንግዶች",
  "Admins": "አስተዳዳሪዎች",
  "Payouts": "ክፍያዎች",
  "Reports": "ሪፖርቶች",
  "Financial Settings": "የፋይናንስ ቅንብሮች",
  "Social Review": "የማህበራዊ መገለጫ ግምገማ",
  "Notifications": "ማሳወቂያዎች",
  "Cashback": "ተመላሽ ገንዘብ",
  "Settings": "ቅንብሮች",
  "More": "ተጨማሪ",
  "Back": "ተመለስ",
  "Account": "መለያ",
  "English": "English",
  "Amharic": "አማርኛ",
  "Language": "ቋንቋ",
  "Sign out": "ውጣ",
  "Sign Out": "ውጣ",
  "Add Profile": "መገለጫ ጨምር",
  "Add a profile": "መገለጫ ጨምር",
  "Switch profile": "መገለጫ ቀይር",
  "Location": "አካባቢ",
  "Social Profiles": "ማህበራዊ መገለጫዎች",
  "Cashier Management": "ገንዘብ ተቀባይ አስተዳደር",
  "Business": "ንግድ",
  "Creator": "ፈጣሪ",
  "Customer": "ደንበኛ",
  "Cashier": "ገንዘብ ተቀባይ",
  "Platform Admin": "የመድረክ አስተዳዳሪ",
  "Operations Admin": "የኦፕሬሽን አስተዳዳሪ",
  "Account setup": "የመለያ ማዋቀር",
  "Business workspace": "የንግድ የሥራ ቦታ",
  "Creator workspace": "የፈጣሪ የሥራ ቦታ",
  "Customer workspace": "የደንበኛ የሥራ ቦታ",
  "Cashier workspace": "የገንዘብ ተቀባይ የሥራ ቦታ",
  "Skip to content": "ወደ ይዘት ዝለል",
  "Create Promotion": "ማስተዋወቂያ ፍጠር",
  "Promotion details": "የማስተዋወቂያ ዝርዝሮች",
  "Promotion title": "የማስተዋወቂያ ርዕስ",
  "Promotion type": "የማስተዋወቂያ ዓይነት",
  "View Only": "እይታ ብቻ",
  "View + Sale": "እይታ + ሽያጭ",
  "UGC": "UGC",
  "UGC + Sale": "UGC + ሽያጭ",
  "What do you want to achieve?": "ምን ማሳካት ይፈልጋሉ?",
  "Application closes": "የማመልከቻ መዝጊያ",
  "Application deadline": "የማመልከቻ ቀነ ገደብ",
  "Content due": "የቪዲዮ ማስረከቢያ ቀን",
  "Video delivery deadline": "የቪዲዮ ማስረከቢያ ቀነ ገደብ",
  "Region": "ክልል",
  "Description": "መግለጫ",
  "Promotion budget": "የማስተዋወቂያ በጀት",
  "Save": "አስቀምጥ",
  "Saving…": "በማስቀመጥ ላይ…",
  "Publish": "አትም",
  "Publishing…": "በማተም ላይ…",
  "Edit": "አርትዕ",
  "Change": "ቀይር",
  "Manage": "አስተዳድር",
  "Approve": "አጽድቅ",
  "Approve Video": "ቪዲዮውን አጽድቅ",
  "Request Changes": "ማስተካከያ ጠይቅ",
  "Reject": "ውድቅ አድርግ",
  "Go Live": "ቀጥታ አድርግ",
  "Going live…": "ቀጥታ በማድረግ ላይ…",
  "Download approved video": "የጸደቀውን ቪዲዮ አውርድ",
  "Feedback (required for changes)": "አስተያየት (ለማስተካከያ ያስፈልጋል)",
  "Overview": "አጠቃላይ እይታ",
  "Creator Applicants": "አመልካች ፈጣሪዎች",
  "Approved Creators": "የጸደቁ ፈጣሪዎች",
  "Funding": "ገንዘብ ድጋፍ",
  "Performance": "አፈጻጸም",
  "Draft": "ረቂቅ",
  "Published": "ታትሟል",
  "Active": "ንቁ",
  "Pending": "በመጠባበቅ ላይ",
  "Approved": "ጸድቋል",
  "Rejected": "ውድቅ ተደርጓል",
  "Completed": "ተጠናቋል",
  "Ready to publish": "ለማተም ዝግጁ",
  "Ready for content": "ለይዘት ዝግጁ",
  "Ready to Go Live": "ቀጥታ ለመሆን ዝግጁ",
  "Available": "የሚገኝ",
  "Reserved": "የተያዘ",
  "Total": "ጠቅላላ",
  "Pending Deposits": "በመጠባበቅ ላይ ያሉ ተቀማጮች",
  "Add Funds": "ገንዘብ ጨምር",
  "Deposits": "ተቀማጮች",
  "Wallet Activity": "የቦርሳ እንቅስቃሴ",
  "Wallet history": "የቦርሳ ታሪክ",
  "Funding requests": "የገንዘብ ጥያቄዎች",
  "Amount": "መጠን",
  "Date": "ቀን",
  "Status": "ሁኔታ",
  "Activity": "እንቅስቃሴ",
  "No activity yet.": "እስካሁን እንቅስቃሴ የለም።",
  "No funding requests": "የገንዘብ ጥያቄ የለም",
  "Business name": "የንግድ ስም",
  "Category": "ምድብ",
  "Days remaining": "የቀሩ ቀናት",
  "Business location": "የንግድ አካባቢ",
  "Promoting Creator": "አስተዋዋቂ ፈጣሪ",
  "Watch Promotion": "ማስተዋወቂያውን ይመልከቱ",
  "Get Directions": "አቅጣጫ ያግኙ",
  "Get Offer QR": "የቅናሽ QR ያግኙ",
  "Use Location": "አካባቢዬን ተጠቀም",
  "Find promotions near you": "በአቅራቢያዎ ማስተዋወቂያዎችን ያግኙ",
  "Discover Promotions": "ማስተዋወቂያዎችን ያግኙ",
  "Search business or creator": "ንግድ ወይም ፈጣሪ ይፈልጉ",
  "All locations": "ሁሉም አካባቢዎች",
  "All promotions": "ሁሉም ማስተዋወቂያዎች",
  "Recommended": "የሚመከር",
  "Nearest": "በጣም ቅርብ",
  "List": "ዝርዝር",
  "Map": "ካርታ",
  "Total Spent": "ጠቅላላ ወጪ",
  "Total Cashback Earned": "ጠቅላላ የተገኘ ተመላሽ ገንዘብ",
  "Total Purchase": "ጠቅላላ ግዢ",
  "Cashback Earned": "የተገኘ ተመላሽ ገንዘብ",
  "No transactions yet.": "እስካሁን ግብይት የለም።",
  "Explore Offers": "ቅናሾችን ያስሱ",
  "Available Earnings": "የሚገኝ ገቢ",
  "Minimum payout": "ዝቅተኛ ክፍያ",
  "Amount remaining": "የቀረው መጠን",
  "Earning History": "የገቢ ታሪክ",
  "Earning Type": "የገቢ ዓይነት",
  "Amount Earned": "የተገኘ መጠን",
  "Date/Time": "ቀን/ሰዓት",
  "Payout Destination": "የክፍያ መድረሻ",
  "Payout method": "የክፍያ ዘዴ",
  "Bank account": "የባንክ ሂሳብ",
  "Bank Name": "የባንክ ስም",
  "Account Number": "የሂሳብ ቁጥር",
  "Verified Weymela phone": "የተረጋገጠ የWeymela ስልክ",
  "Save Destination": "መድረሻውን አስቀምጥ",
  "How You Earn": "እንዴት ገቢ እንደሚያገኙ",
  "Verified Views": "የተረጋገጡ እይታዎች",
  "Sale Commission": "የሽያጭ ኮሚሽን",
  "Search": "ፈልግ",
  "Search business or promotion...": "ንግድ ወይም ማስተዋወቂያ ይፈልጉ...",
  "Deposit review": "የተቀማጭ ግምገማ",
  "View Receipt": "ደረሰኝ ይመልከቱ",
  "Review": "ገምግም",
  "Business wallets": "የንግድ ቦርሳዎች",
  "History": "ታሪክ",
  "Pay": "ክፈል",
  "Remaining": "የቀረ",
  "Method": "ዘዴ",
  "Name": "ስም",
  "Try again": "እንደገና ይሞክሩ",
  "Loading your workspace…": "የሥራ ቦታዎን በመጫን ላይ…",
  "Preparing your secure session…": "ደህንነቱ የተጠበቀ ክፍለ ጊዜዎን በማዘጋጀት ላይ…",
  "Workspace unavailable": "የሥራ ቦታው አይገኝም",
  "Sign in": "ይግቡ",
  "Continue": "ቀጥል",
  "Cancel": "ይቅር",
  "Close": "ዝጋ",
  "New Promotion request": "አዲስ የማስተዋወቂያ ጥያቄ",
  "Promotion request approved": "የማስተዋወቂያ ጥያቄው ጸድቋል",
  "Promotion request reviewed": "የማስተዋወቂያ ጥያቄው ተገምግሟል",
  "Promotion funding confirmed": "የማስተዋወቂያ ገንዘብ ተረጋግጧል",
  "Promotion published": "ማስተዋወቂያው ታትሟል",
  "Video ready for review": "ቪዲዮው ለግምገማ ዝግጁ ነው",
  "Video approved": "ቪዲዮው ጸድቋል",
  "Video changes requested": "የቪዲዮ ማስተካከያ ተጠይቋል",
  "Video reviewed": "ቪዲዮው ተገምግሟል",
  "Publication awaiting verification": "ህትመቱ ማረጋገጫ በመጠባበቅ ላይ ነው",
  "Publication verified": "ህትመቱ ተረጋግጧል",
  "Publication verification failed": "የህትመት ማረጋገጫው አልተሳካም",
  "Creator work is Live": "የፈጣሪው ሥራ ቀጥታ ሆኗል",
  "New UGC request": "አዲስ የUGC ጥያቄ",
  "UGC request approved": "የUGC ጥያቄው ጸድቋል",
  "UGC request reviewed": "የUGC ጥያቄው ተገምግሟል",
  "UGC content submitted": "የUGC ይዘት ገብቷል",
  "UGC changes requested": "የUGC ማስተካከያ ተጠይቋል",
  "UGC content approved": "የUGC ይዘት ጸድቋል",
  "UGC content reviewed": "የUGC ይዘት ተገምግሟል",
  "Deposit awaiting review": "ተቀማጩ ግምገማ በመጠባበቅ ላይ ነው",
  "Deposit approved": "ተቀማጩ ጸድቋል",
  "Deposit reviewed": "ተቀማጩ ተገምግሟል",
  "Eligible for payout": "ለክፍያ ብቁ ሆኗል",
  "Payout confirmed": "ክፍያው ተረጋግጧል",
  "View Reward earned": "የእይታ ሽልማት ተገኝቷል",
  "Purchase earning recorded": "የግዢ ገቢ ተመዝግቧል",
  "Purchase cashback recorded": "የግዢ ተመላሽ ገንዘብ ተመዝግቧል",
  "A Creator has requested to join your Promotion.": "አንድ ፈጣሪ ማስተዋወቂያዎን ለመቀላቀል ጠይቋል።",
  "Your request was approved. Open your Promotion for next steps.": "ጥያቄዎ ጸድቋል። ቀጣዩን እርምጃ ለማየት ማስተዋወቂያውን ይክፈቱ።",
  "The Business did not approve this request. You can discover other eligible Promotions.": "ንግዱ ጥያቄውን አላጸደቀም። ሌሎች ብቁ ማስተዋወቂያዎችን ማግኘት ይችላሉ።",
  "Open your Promotion to review your saved budget and next steps.": "የተቀመጠውን በጀትና ቀጣዩን እርምጃ ለማየት ማስተዋወቂያዎን ይክፈቱ።",
  "The Promotion workspace shows the saved status.": "የማስተዋወቂያ የሥራ ቦታው የተቀመጠውን ሁኔታ ያሳያል።",
  "Verified activity has added to your earnings.": "የተረጋገጠ እንቅስቃሴ ወደ ገቢዎ ተጨምሯል።",
  "Your available balance has reached the current minimum to cash out.": "ያለዎት ቀሪ ሂሳብ ለክፍያ የሚያስፈልገው ዝቅተኛ መጠን ደርሷል።",
  "Admin has recorded your confirmed external payment. Remaining funds carry forward.": "አስተዳዳሪው የተረጋገጠውን ውጫዊ ክፍያ መዝግቧል። ቀሪው ገንዘብ ይተላለፋል።",
  "A Business submitted a payment receipt. Review it against the payment record before approval.": "አንድ ንግድ የክፍያ ደረሰኝ አስገብቷል። ከማጽደቅዎ በፊት ከክፍያ መዝገቡ ጋር ያረጋግጡ።",
  "Your saved wallet balance includes the approved funds.": "የተቀመጠው የቦርሳ ቀሪ ሂሳብዎ የጸደቀውን ገንዘብ ያካትታል።",
  "The deposit was not approved. No funds were credited.": "ተቀማጩ አልጸደቀም። ምንም ገንዘብ አልተጨመረም።",
  "A Creator requested to join your UGC opportunity.": "አንድ ፈጣሪ የUGC እድልዎን ለመቀላቀል ጠይቋል።",
  "Open your UGC assignment to review the requirements.": "መስፈርቶቹን ለማየት የUGC ሥራዎን ይክፈቱ።",
  "The Business did not approve this UGC request.": "ንግዱ ይህን የUGC ጥያቄ አላጸደቀም።",
  "A Creator submitted content for your review.": "አንድ ፈጣሪ ለግምገማዎ ይዘት አስገብቷል።",
  "Review the Business feedback and resubmit your content.": "የንግዱን አስተያየት ይመልከቱና ይዘትዎን እንደገና ያስገቡ።",
  "Your UGC content was approved and your earning was recorded.": "የUGC ይዘትዎ ጸድቋል እና ገቢዎ ተመዝግቧል።",
  "The Business did not approve this content.": "ንግዱ ይህን ይዘት አላጸደቀም።",
  "A Creator submitted a private revision for your review.": "አንድ ፈጣሪ ለግምገማዎ የግል ማሻሻያ አስገብቷል።",
  "Your approved revision is ready to publish.": "የጸደቀው ማሻሻያዎ ለማተም ዝግጁ ነው።",
  "Review the Business feedback and submit a new revision.": "የንግዱን አስተያየት ይመልከቱና አዲስ ማሻሻያ ያስገቡ።",
  "Confirm the public post and selected Creator profile.": "የሕዝብ ህትመቱንና የተመረጠውን የፈጣሪ መገለጫ ያረጋግጡ።",
  "Your verified publication is ready to Go Live.": "የተረጋገጠው ህትመትዎ ቀጥታ ለመሆን ዝግጁ ነው።",
  "The verified Creator publication is now live.": "የተረጋገጠው የፈጣሪ ህትመት አሁን ቀጥታ ነው።",
  "Deposited to": "ተቀማጭ የተደረገበት",
  "Payment receipt": "የክፍያ ደረሰኝ",
  "Upload Receipt": "ደረሰኝ ይስቀሉ",
  "Submit for Review": "ለግምገማ ያስገቡ",
  "Creator payment (ETB)": "የፈጣሪ ክፍያ (ETB)",
  "Creators needed": "የሚያስፈልጉ ፈጣሪዎች",
  "Instructions": "መመሪያዎች",
  "Product arrangement": "የምርት ዝግጅት",
  "Deliver content only": "ይዘት ብቻ ያስረክቡ",
  "Creator must post": "ፈጣሪው ማተም አለበት",
  "Request to Join": "ለመቀላቀል ይጠይቁ",
  "Submit Revised Content": "የተሻሻለ ይዘት ያስገቡ",
  "Private review copy": "የግል ግምገማ ቅጂ",
};

function readLanguage(): Language {
  if (typeof window === "undefined") return "en";
  return window.localStorage.getItem(storageKey) === "am" ? "am" : "en";
}

let currentLanguage: Language = readLanguage();

export function languageLocale() {
  return currentLanguage === "am" ? "am-ET" : "en-ET";
}

export function translateText(value: string, language = currentLanguage) {
  if (language === "en" || !value.trim()) return value;
  const leading = value.match(/^\s*/)?.[0] ?? "";
  const trailing = value.match(/\s*$/)?.[0] ?? "";
  const source = value.trim();
  const exact = amharic[source];
  if (exact) return `${leading}${exact}${trailing}`;
  const patterns: [RegExp, (...matches: string[]) => string][] = [
    [/^(\d+) days left$/, days => `${days} ቀናት ቀርተዋል`],
    [/^(\d+) offers?$/, count => `${count} ቅናሾች`],
    [/^By (.+)$/, name => `በ${name}`],
    [/^Recipient: (.+)$/, name => `ተቀባይ፦ ${name}`],
    [/^Current account: (.+)$/, account => `የአሁኑ ሂሳብ፦ ${account}`],
    [/^(\d+) unread notifications$/, count => `${count} ያልተነበቡ ማሳወቂያዎች`],
    [/^Revision (\d+)$/, revision => `ማሻሻያ ${revision}`],
    [/^Amount remaining:$/, () => "የቀረው መጠን፦"],
    [/^You earned (.+) ETB from an eligible purchase\.$/, value => `ከብቁ ግዢ ${value} ETB አግኝተዋል።`],
    [/^Your (.+) ETB purchase earned (.+) ETB cashback\.$/, (purchase, cashback) => `የ${purchase} ETB ግዢዎ ${cashback} ETB ተመላሽ ገንዘብ አስገኝቷል።`],
    [/^New sale — (.+) ETB\.$/, value => `አዲስ ሽያጭ — ${value} ETB።`],
  ];
  for (const [pattern, render] of patterns) {
    const match = source.match(pattern);
    if (match) return `${leading}${render(...match.slice(1))}${trailing}`;
  }
  return value;
}

const originals = new WeakMap<Node, string>();
const attributeOriginals = new WeakMap<Element, Map<string, string>>();
const translatedAttributes = ["aria-label", "placeholder", "title"];

function excluded(node: Node) {
  const parent = node instanceof Element ? node : node.parentElement;
  return parent?.closest("script,style,code,[data-no-translate]") != null;
}

function localizeTextNode(node: Text) {
  if (excluded(node)) return;
  const previous = originals.get(node);
  const translated = previous == null ? null : translateText(previous, "am");
  if (previous == null || (node.data !== previous && node.data !== translated)) originals.set(node, node.data);
  const original = originals.get(node) ?? node.data;
  const next = currentLanguage === "en" ? original : translateText(original, currentLanguage);
  if (node.data !== next) node.data = next;
}

function localizeElement(element: Element) {
  if (excluded(element)) return;
  let values = attributeOriginals.get(element);
  if (!values) {
    values = new Map<string, string>();
    attributeOriginals.set(element, values);
  }
  for (const attribute of translatedAttributes) {
    const value = element.getAttribute(attribute);
    if (value == null) continue;
    const previous = values.get(attribute);
    const translated = previous == null ? null : translateText(previous, "am");
    if (previous == null || (value !== previous && value !== translated)) values.set(attribute, value);
    const original = values.get(attribute) ?? value;
    const next = currentLanguage === "en" ? original : translateText(original, currentLanguage);
    if (value !== next) element.setAttribute(attribute, next);
  }
}

function localize(root: Node) {
  if (root.nodeType === Node.TEXT_NODE) localizeTextNode(root as Text);
  if (root instanceof Element) localizeElement(root);
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  let node = walker.nextNode();
  while (node) {
    if (node.nodeType === Node.TEXT_NODE) localizeTextNode(node as Text);
    else localizeElement(node as Element);
    node = walker.nextNode();
  }
}

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(readLanguage);
  currentLanguage = language;
  const setLanguage = (next: Language) => {
    window.localStorage.setItem(storageKey, next);
    currentLanguage = next;
    setLanguageState(next);
  };
  useLayoutEffect(() => {
    currentLanguage = language;
    document.documentElement.lang = language === "am" ? "am" : "en";
    localize(document.body);
  }, [language]);
  useEffect(() => {
    let applying = false;
    const observer = new MutationObserver(records => {
      if (applying) return;
      applying = true;
      for (const record of records) {
        if (record.type === "characterData") localize(record.target);
        for (const node of record.addedNodes) localize(node);
        if (record.type === "attributes") localize(record.target);
      }
      applying = false;
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: translatedAttributes });
    return () => observer.disconnect();
  }, []);
  return <LanguageContext.Provider value={{ language, setLanguage }}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  return useContext(LanguageContext);
}

export function LanguageChoice() {
  const { language, setLanguage } = useLanguage();
  return <div className="language-choice" role="group" aria-label="Language">
    <span>Language</span>
    <div>
      <button type="button" aria-pressed={language === "en"} onClick={() => setLanguage("en")}>English</button>
      <button type="button" aria-pressed={language === "am"} onClick={() => setLanguage("am")}>አማርኛ</button>
    </div>
  </div>;
}
