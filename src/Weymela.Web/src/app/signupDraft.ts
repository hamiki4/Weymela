export type PublicSignupRole = "Customer" | "Business" | "Creator";

export type PublicSignupDraft = {
  version: 1;
  idempotencyKey: string;
  role: PublicSignupRole;
  legalName: string;
  email: string;
  phone: string;
  businessName?: string;
  businessType?: string;
  socialPlatform?: string;
  socialProfileUrl?: string;
  followerCount?: number;
  subscriberCount?: number | null;
  legalAccepted: true;
};

const key = "weymela.public-signup";

export function saveSignupDraft(draft: PublicSignupDraft) {
  window.sessionStorage.setItem(key, JSON.stringify(draft));
}

export function readSignupDraft(): PublicSignupDraft | null {
  try {
    const parsed = JSON.parse(window.sessionStorage.getItem(key) ?? "null") as Partial<PublicSignupDraft> | null;
    if (!parsed || parsed.version !== 1 || !parsed.idempotencyKey
        || !["Customer", "Business", "Creator"].includes(parsed.role ?? "")
        || !parsed.legalName || !parsed.email || !parsed.phone || parsed.legalAccepted !== true)
      return null;
    return parsed as PublicSignupDraft;
  } catch {
    return null;
  }
}

export function clearSignupDraft() {
  window.sessionStorage.removeItem(key);
}

export function hasSignupDraft() {
  return readSignupDraft() !== null;
}
