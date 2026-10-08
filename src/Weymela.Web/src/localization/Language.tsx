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
  "Promotions": "ማስታወቂያዎች",
  "Promotion": "ማስታወቂያ",
  "Price": "ዋጋ",
  "Campaigns": "ዘመቻዎች",
  "Requests": "ጥያቄዎች",
  "Wallet": "የገንዘብ ቦርሳ",
  "Wallets": "የገንዘብ ቦርሳዎች",
  "Transactions": "ግብይቶች",
  "Checkout": "ክፍያ ማረጋገጫ",
  "Purchase": "ግዢ",
  "Profile": "መገለጫ",
  "Earnings": "ገቢዎች",
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
  "Help": "እገዛ",
  "Contact Us": "ያግኙን",
  "Delete Account": "መለያ ሰርዝ",
  "More": "ተጨማሪ",
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
  "Create Promotion": "ማስታወቂያ ፍጠር",
  "Promotion details": "የማስታወቂያ ዝርዝሮች",
  "Promotion title": "የማስታወቂያ ርዕስ",
  "Promotion type": "የማስታወቂያ ዓይነት",
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
  "Promotion budget": "የማስታወቂያ በጀት",
  "Save": "አስቀምጥ",
  "Saving…": "በማስቀመጥ ላይ…",
  "Publish": "አትም",
  "Publishing…": "በማተም ላይ…",
  "Edit": "አርትዕ",
  "Confirm": "አረጋግጥ",
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
  "Watch Promotion": "ማስታወቂያውን ይመልከቱ",
  "Get Directions": "አቅጣጫ ያግኙ",
  "Get Offer QR": "የቅናሽ QR ያግኙ",
  "Use Location": "አካባቢዬን ተጠቀም",
  "Find promotions near you": "በአቅራቢያዎ ማስታወቂያዎችን ያግኙ",
  "Discover Promotions": "ማስታወቂያዎችን ያግኙ",
  "Search business or creator": "ንግድ ወይም ፈጣሪ ይፈልጉ",
  "All locations": "ሁሉም አካባቢዎች",
  "All promotions": "ሁሉም ማስታወቂያዎች",
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
  "Search business or promotion...": "ንግድ ወይም ማስታወቂያ ይፈልጉ...",
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
  "Cancel": "ሰርዝ",
  "Close": "ዝጋ",
  "New Promotion request": "አዲስ የማስታወቂያ ጥያቄ",
  "Promotion request approved": "የማስታወቂያ ጥያቄው ጸድቋል",
  "Promotion request reviewed": "የማስታወቂያ ጥያቄው ተገምግሟል",
  "Promotion funding confirmed": "የማስታወቂያ ገንዘብ ተረጋግጧል",
  "Promotion published": "ማስታወቂያው ታትሟል",
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
  "A Creator has requested to join your Promotion.": "አንድ ፈጣሪ ማስታወቂያዎን ለመቀላቀል ጠይቋል።",
  "Your request was approved. Open your Promotion for next steps.": "ጥያቄዎ ጸድቋል። ቀጣዩን እርምጃ ለማየት ማስታወቂያውን ይክፈቱ።",
  "The Business did not approve this request. You can discover other eligible Promotions.": "ንግዱ ጥያቄውን አላጸደቀም። ሌሎች ብቁ ማስታወቂያዎችን ማግኘት ይችላሉ።",
  "Open your Promotion to review your saved budget and next steps.": "የተቀመጠውን በጀትና ቀጣዩን እርምጃ ለማየት ማስታወቂያዎን ይክፈቱ።",
  "The Promotion workspace shows the saved status.": "የማስታወቂያ የሥራ ቦታው የተቀመጠውን ሁኔታ ያሳያል።",
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
  "Open Settings": "ቅንብሮችን ክፈት",
  "Settings options": "የቅንብሮች አማራጮች",
  "Account profiles": "የመለያ መገለጫዎች",
  "Close one account role safely": "አንድ የመለያ ሚናን በደህና ይዝጉ",
  "Back": "ተመለስ",
  "Weymela Support": "የWeymela ድጋፍ",
  "Email": "ኢሜይል",
  "Phone": "ስልክ",
  "Customer Help": "የደንበኛ እገዛ",
  "Creator Help": "የፈጣሪ እገዛ",
  "Business Help": "የንግድ እገዛ",
  "Platform Admin Help": "የመድረክ አስተዳዳሪ እገዛ",
  "Operations Admin Help": "የኦፕሬሽን አስተዳዳሪ እገዛ",
  "Cashier Help": "የገንዘብ ተቀባይ እገዛ",
  "Discover promotions": "ማስታወቂያዎችን ያግኙ",
  "Open Discover to find active offers from real Businesses and Creators.": "ከንግዶችና ፈጣሪዎች የቀረቡ ንቁ ቅናሾችን ለማግኘት ‘ያግኙ’ ገጹን ይክፈቱ።",
  "Watch promotions": "ማስታወቂያዎችን ይመልከቱ",
  "Open a Promotion card and choose Watch Promotion.": "የማስታወቂያ ካርድን ይክፈቱና ‘ማስታወቂያውን ይመልከቱ’ የሚለውን ይምረጡ።",
  "Get an offer QR": "የቅናሽ QR ያግኙ",
  "Choose Get Offer QR on an eligible sale offer and show it at checkout.": "ብቁ የሽያጭ ቅናሽ ላይ ‘የቅናሽ QR ያግኙ’ የሚለውን ይምረጡና ሲከፍሉ ያሳዩ።",
  "How cashback works": "ተመላሽ ገንዘብ እንዴት ይሰራል",
  "You pay the full purchase amount. Eligible cashback is added separately to your Cashback wallet.": "የግዢውን ሙሉ መጠን ይከፍላሉ። ብቁ ተመላሽ ገንዘብ ለብቻው ወደ ተመላሽ ገንዘብ ቦርሳዎ ይጨመራል።",
  "Set a payout destination": "የክፍያ መድረሻ ያዘጋጁ",
  "Open Cashback and save your verified Telebirr phone or bank account.": "ተመላሽ ገንዘብን ይክፈቱና የተረጋገጠውን የቴሌብር ስልክ ወይም የባንክ ሂሳብ ያስቀምጡ።",
  "View transactions": "ግብይቶችን ይመልከቱ",
  "Transactions shows the Business, full purchase amount, cashback, and date.": "ግብይቶች የንግዱን ስም፣ ሙሉ የግዢ መጠን፣ ተመላሽ ገንዘብና ቀን ያሳያል።",
  "Delete an account": "መለያ ይሰርዙ",
  "Open Settings, choose Delete Account, then select only the role you want to close.": "ቅንብሮችን ይክፈቱ፣ ‘መለያ ሰርዝ’ የሚለውን ይምረጡ፣ ከዚያ መዝጋት የሚፈልጉትን ሚና ብቻ ይምረጡ።",
  "Find opportunities": "ዕድሎችን ያግኙ",
  "Open Discover and choose an eligible Promotion.": "‘ያግኙ’ ገጹን ይክፈቱና ብቁ ማስታወቂያ ይምረጡ።",
  "Apply": "ያመልክቱ",
  "Apply before the application deadline and wait for the Business decision.": "ከማመልከቻ ቀነ ገደቡ በፊት ያመልክቱና የንግዱን ውሳኔ ይጠብቁ።",
  "Business approval": "የንግድ ማጽደቅ",
  "An approval lets you continue to the requested video or publication step.": "ማጽደቁ ወደተጠየቀው ቪዲዮ ወይም ህትመት ደረጃ እንዲቀጥሉ ያስችላል።",
  "Upload a video": "ቪዲዮ ይስቀሉ",
  "Open the approved Promotion and upload the requested private review copy before the deadline.": "የጸደቀውን ማስታወቂያ ይክፈቱና ከቀነ ገደቡ በፊት የተጠየቀውን የግል ግምገማ ቅጂ ይስቀሉ።",
  "Respond to changes": "የተጠየቁ ማስተካከያዎችን ያድርጉ",
  "Read the Business comment, update the video, and submit a new revision.": "የንግዱን አስተያየት ያንብቡ፣ ቪዲዮውን ያስተካክሉና አዲስ ማሻሻያ ያስገቡ።",
  "After approval and any required publication check, open the Promotion and choose Go Live.": "ከማጽደቅና ከሚያስፈልገው የህትመት ማረጋገጫ በኋላ ማስታወቂያውን ይክፈቱና ‘ቀጥታ አድርግ’ ይምረጡ።",
  "Earnings and payouts": "ገቢዎችና ክፍያዎች",
  "Verified earnings appear in Earnings. Eligible balances enter the Admin payout queue automatically.": "የተረጋገጡ ገቢዎች በገቢዎች ገጽ ይታያሉ። ብቁ ቀሪ ሂሳቦች በራስ-ሰር ወደ አስተዳዳሪ የክፍያ ወረፋ ይገባሉ።",
  "Add funds": "ገንዘብ ይጨምሩ",
  "Open Wallet, choose a receiving bank, enter the amount, and upload the receipt.": "የገንዘብ ቦርሳን ይክፈቱ፣ ተቀባይ ባንክ ይምረጡ፣ መጠኑን ያስገቡና ደረሰኙን ይስቀሉ።",
  "Deposit receipt": "የተቀማጭ ደረሰኝ",
  "The deposit remains Pending until an authorized Admin approves the receipt.": "ፈቃድ ያለው አስተዳዳሪ ደረሰኙን እስኪያጸድቅ ድረስ ተቀማጩ በመጠባበቅ ላይ ይቆያል።",
  "Create and publish": "ይፍጠሩና ያትሙ",
  "Create a Promotion, save changes, then Publish after funding and deadlines are valid.": "ማስታወቂያ ይፍጠሩ፣ ለውጦቹን ያስቀምጡ፣ ገንዘቡና ቀነ ገደቦቹ ከተረጋገጡ በኋላ ያትሙ።",
  "Approve Creators": "ፈጣሪዎችን ያጽድቁ",
  "Open Requests to approve or reject Creator applications.": "የፈጣሪ ማመልከቻዎችን ለማጽደቅ ወይም ውድቅ ለማድረግ ጥያቄዎችን ይክፈቱ።",
  "Review videos": "ቪዲዮዎችን ይገምግሙ",
  "Watch each submitted revision, then Approve, Request Changes, or Reject.": "እያንዳንዱን የገባ ማሻሻያ ይመልከቱ፤ ከዚያ ያጽድቁ፣ ማስተካከያ ይጠይቁ ወይም ውድቅ ያድርጉ።",
  "Manage checkout": "ክፍያ ማረጋገጫን ያስተዳድሩ",
  "Use Checkout for eligible customer purchases and keep Cashier access limited to this Business.": "ለብቁ የደንበኛ ግዢዎች ክፍያ ማረጋገጫን ይጠቀሙ፤ የገንዘብ ተቀባዩን መዳረሻ ለዚህ ንግድ ብቻ ይገድቡ።",
  "Transactions shows Promotion, total purchase, customer cashback, and date.": "ግብይቶች ማስታወቂያውን፣ ጠቅላላ ግዢውን፣ የደንበኛ ተመላሽ ገንዘብና ቀን ያሳያል።",
  "Review deposits": "ተቀማጮችን ይገምግሙ",
  "Open Wallets and approve only receipts verified against the receiving account.": "የገንዘብ ቦርሳዎችን ይክፈቱ፤ ከተቀባይ ሂሳቡ ጋር የተረጋገጡ ደረሰኞችን ብቻ ያጽድቁ።",
  "Open Wallets and review only the deposits authorized for Operations.": "የገንዘብ ቦርሳዎችን ይክፈቱ፤ ለኦፕሬሽን የተፈቀዱ ተቀማጮችን ብቻ ይገምግሙ።",
  "Manage payouts": "ክፍያዎችን ያስተዳድሩ",
  "Open Payouts, choose an eligible account, and enter an amount within the allowed balance.": "ክፍያዎችን ይክፈቱ፣ ብቁ መለያ ይምረጡና በተፈቀደው ቀሪ ሂሳብ ውስጥ መጠን ያስገቡ።",
  "Open Payouts to review eligible accounts and authorized payment activity.": "ብቁ መለያዎችንና የተፈቀዱ የክፍያ እንቅስቃሴዎችን ለመገምገም ክፍያዎችን ይክፈቱ።",
  "Review Promotions and UGC": "ማስታወቂያዎችንና UGCን ይገምግሙ",
  "Use Campaigns and UGC to review milestones, exceptions, and approved deliverables.": "ዋና ደረጃዎችን፣ ልዩ ሁኔታዎችንና የጸደቁ ሥራዎችን ለመገምገም ዘመቻዎችንና UGCን ይጠቀሙ።",
  "Use Campaigns and UGC to review assigned milestones and exceptions.": "የተመደቡ ዋና ደረጃዎችንና ልዩ ሁኔታዎችን ለመገምገም ዘመቻዎችንና UGCን ይጠቀሙ።",
  "Manage account roles": "የመለያ ሚናዎችን ያስተዳድሩ",
  "Use Accounts to create or close roles while preserving succession and ownership rules.": "የተተኪነትና የባለቤትነት ደንቦችን በመጠበቅ ሚናዎችን ለመፍጠር ወይም ለመዝጋት መለያዎችን ይጠቀሙ።",
  "Review profile requests within Operations authority. Platform Admin controls privileged roles.": "በኦፕሬሽን ስልጣን ውስጥ ያሉ የመገለጫ ጥያቄዎችን ይገምግሙ። ልዩ ፈቃድ ያላቸውን ሚናዎች የመድረክ አስተዳዳሪ ይቆጣጠራል።",
  "Review financial activity": "የፋይናንስ እንቅስቃሴን ይገምግሙ",
  "Use Reports and audit views to confirm balanced journals and authorized activity.": "የተመጣጠኑ ሂሳብ መዝገቦችንና የተፈቀደ እንቅስቃሴን ለማረጋገጥ ሪፖርቶችንና የኦዲት እይታዎችን ይጠቀሙ።",
  "Use the authorized reports and audit details for operational checks.": "ለኦፕሬሽን ማረጋገጫዎች የተፈቀዱ ሪፖርቶችንና የኦዲት ዝርዝሮችን ይጠቀሙ።",
  "Complete a purchase": "ግዢ ያጠናቅቁ",
  "Open Purchase, scan the eligible offer QR, enter the full purchase amount, and confirm.": "ግዢን ይክፈቱ፣ ብቁ የቅናሽ QRን ይቃኙ፣ ሙሉ የግዢ መጠኑን ያስገቡና ያረጋግጡ።",
  "Transactions shows purchases completed for your Business.": "ግብይቶች ለንግድዎ የተጠናቀቁ ግዢዎችን ያሳያል።",
  "Open Settings and choose Delete Account. Business ownership and financial records remain protected.": "ቅንብሮችን ይክፈቱና ‘መለያ ሰርዝ’ ይምረጡ። የንግድ ባለቤትነትና የፋይናንስ መዝገቦች እንደተጠበቁ ይቆያሉ።",
  "Choose an account role": "የመለያ ሚና ይምረጡ",
  "Only the selected role will close. Your other roles, money, and required records are not deleted.": "የተመረጠው ሚና ብቻ ይዘጋል። ሌሎች ሚናዎችዎ፣ ገንዘብዎና አስፈላጊ መዝገቦችዎ አይሰረዙም።",
  "Account roles": "የመለያ ሚናዎች",
  "Eligible": "ብቁ",
  "Action Required": "እርምጃ ያስፈልጋል",
  "Pending Closure": "መዘጋት በመጠባበቅ ላይ",
  "Before this role can close": "ይህ ሚና ከመዘጋቱ በፊት",
  "This role can close without changing your other account roles.": "ይህ ሚና ሌሎች የመለያ ሚናዎችዎን ሳይቀይር መዘጋት ይችላል።",
  "Closure Pending": "መዘጋት በመጠባበቅ ላይ",
  "Request Closure": "መዘጋትን ጠይቅ",
  "Confirm account closure": "የመለያ መዘጋትን ያረጋግጡ",
  "You are closing only the": "የሚዘጉት",
  "role.": "ሚናን ብቻ ነው።",
  "Required financial, security, tax, and audit records will be preserved. If obligations remain, the role stays restricted or active until an authorized review completes.": "አስፈላጊ የፋይናንስ፣ የደህንነት፣ የግብርና የኦዲት መዝገቦች ይጠበቃሉ። ግዴታዎች ካሉ ፈቃድ ያለው ግምገማ እስኪጠናቀቅ ሚናው የተገደበ ወይም ንቁ ሆኖ ይቆያል።",
  "I understand and want to close this role.": "ተረድቻለሁ፤ ይህን ሚና መዝጋት እፈልጋለሁ።",
  "Closing…": "በመዝጋት ላይ…",
  "Closure is pending. Resolve the items shown below; your financial and audit records remain protected.": "መዘጋቱ በመጠባበቅ ላይ ነው። ከታች የታዩትን ጉዳዮች ይፍቱ፤ የፋይናንስና የኦዲት መዝገቦችዎ እንደተጠበቁ ይቆያሉ።",
  "Sign in again before closing an account role.": "የመለያ ሚናን ከመዝጋትዎ በፊት እንደገና ይግቡ።",
  "Sign in again before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት እንደገና ይግቡ።",
  "Choose one of your active account roles.": "ንቁ ከሆኑት የመለያ ሚናዎችዎ አንዱን ይምረጡ።",
  "Confirm that you want to close this role.": "ይህን ሚና መዝጋት እንደሚፈልጉ ያረጋግጡ።",
  "This account role was not found.": "ይህ የመለያ ሚና አልተገኘም።",
  "This account role is not active.": "ይህ የመለያ ሚና ንቁ አይደለም።",
  "We could not complete that request.": "ጥያቄውን ማጠናቀቅ አልተቻለም።",
  "Receive the remaining cashback before closing this Customer role.": "ይህን የደንበኛ ሚና ከመዝጋትዎ በፊት የቀረውን ተመላሽ ገንዘብ ይቀበሉ።",
  "Complete the pending Customer payout before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት በመጠባበቅ ላይ ያለውን የደንበኛ ክፍያ ያጠናቅቁ።",
  "Receive the remaining Creator earnings before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት የቀሩትን የፈጣሪ ገቢዎች ይቀበሉ።",
  "Complete the pending Creator payout before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት በመጠባበቅ ላይ ያለውን የፈጣሪ ክፍያ ያጠናቅቁ።",
  "Resolve pending Promotion applications before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት በመጠባበቅ ላይ ያሉ የማስታወቂያ ማመልከቻዎችን ይፍቱ።",
  "Complete active Promotion work before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት ንቁ የማስታወቂያ ሥራን ያጠናቅቁ።",
  "Resolve pending UGC requests before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት በመጠባበቅ ላይ ያሉ የUGC ጥያቄዎችን ይፍቱ።",
  "Complete outstanding UGC work before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት ያልተጠናቀቀ የUGC ሥራን ያጠናቅቁ።",
  "Resolve all available and reserved Business funds before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት ሁሉንም ያሉና የተያዙ የንግድ ገንዘቦች ይፍቱ።",
  "Resolve pending deposits before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት በመጠባበቅ ላይ ያሉ ተቀማጮችን ይፍቱ።",
  "Complete or cancel active funded Promotions before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት ገንዘብ የተመደበላቸውን ንቁ ማስታወቂያዎች ያጠናቅቁ ወይም ይሰርዙ።",
  "Complete active UGC opportunities before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት ንቁ የUGC ዕድሎችን ያጠናቅቁ።",
  "Deactivate or transfer Business Cashiers before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት የንግድ ገንዘብ ተቀባዮችን ያቦዝኑ ወይም ያስተላልፉ።",
  "Transfer Business ownership to another approved owner before closing this role.": "ይህን ሚና ከመዝጋትዎ በፊት የንግድ ባለቤትነትን ለሌላ የጸደቀ ባለቤት ያስተላልፉ።",
  "Create and verify a replacement Platform Admin before requesting closure.": "መዘጋትን ከመጠየቅዎ በፊት ተተኪ የመድረክ አስተዳዳሪ ይፍጠሩና ያረጋግጡ።",
  "Another Platform Admin must review this privileged role closure.": "ሌላ የመድረክ አስተዳዳሪ የዚህን ልዩ ፈቃድ ያለው ሚና መዘጋት መገምገም አለበት።",
  "A Platform Admin must review this privileged role closure.": "የመድረክ አስተዳዳሪ የዚህን ልዩ ፈቃድ ያለው ሚና መዘጋት መገምገም አለበት።",
  "Secure sign-in deletion is temporarily unavailable. Your role will remain active until an administrator can complete it.": "የደህንነት መግቢያ መሰረዝ ለጊዜው አይገኝም። አስተዳዳሪ እስኪያጠናቅቀው ድረስ ሚናዎ ንቁ ሆኖ ይቆያል።",
  "Account role closed": "የመለያ ሚና ተዘግቷል",
  "Account closure needs attention": "የመለያ መዘጋት እርምጃ ይፈልጋል",
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
    [/^The (.+) account role was closed\. Any other account roles remain available\.$/, role => `${role} የመለያ ሚና ተዘግቷል። ሌሎች የመለያ ሚናዎች ካሉ መጠቀም ይችላሉ።`],
    [/^Your (.+) role cannot close yet\. Review the items shown in Settings\.$/, role => `${role} ሚናዎ እስካሁን ሊዘጋ አይችልም። በቅንብሮች የታዩትን ጉዳዮች ይመልከቱ።`],
    [/^(.+) role closed\. Your other account roles are unchanged\.$/, role => `${role} ሚና ተዘግቷል። ሌሎች የመለያ ሚናዎችዎ አልተቀየሩም።`],
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
