import type { AccountLegalStatus } from "../api/types";
import { useLanguage } from "../localization/Language";

export const policyAgreement = "I agree to Weymela's Terms of Service and Privacy Policy.";

export function PolicyConsent({ id, checked, onChange, documents = [], disabled = false }: {
  id: string; checked: boolean; onChange: (accepted: boolean) => void;
  documents?: AccountLegalStatus["documents"]; disabled?: boolean;
}) {
  const { language } = useLanguage();
  const suffix = language === "am" ? "?language=am" : "";
  return <label htmlFor={id} className="signup-legal-consent">
    <input id={id} type="checkbox" aria-label={policyAgreement} required={!disabled} checked={checked}
      disabled={disabled} onChange={event => onChange(event.target.checked)} />
    <span>I agree to Weymela&apos;s <a href={documents.find(document => document.kind === "TermsOfService")?.viewPath
      ?? `/legal/terms-of-service${suffix}`} target="_blank" rel="noreferrer">Terms of Service</a> and <a
      href={documents.find(document => document.kind === "PrivacyPolicy")?.viewPath ?? `/legal/privacy-policy${suffix}`}
      target="_blank" rel="noreferrer">Privacy Policy</a>.</span>
  </label>;
}
