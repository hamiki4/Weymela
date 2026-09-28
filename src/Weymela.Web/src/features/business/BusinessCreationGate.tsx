import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { useResource } from "../../api/client";
import type { Wallet } from "../../api/types";
import { Button, Notice, Resource, Section } from "../../ui/components";

type RequiredDocument = {
  id: string;
  type: string;
  version: string;
  contentHash: string;
  accepted: boolean;
};

export function BusinessCreationGate({ children }: { children: (wallet: Wallet) => ReactNode }) {
  const legal = useResource<RequiredDocument[]>("/legal/current");
  const wallet = useResource<Wallet>("/business/wallet");
  if (legal.loading || wallet.loading)
    return <p className="loading" role="status">Checking Business eligibility and available funds…</p>;
  if (legal.error || wallet.error)
    return <Section title="Creation unavailable"><Notice error>{legal.error?.message ?? wallet.error?.message}</Notice><Button variant="secondary" onClick={() => { legal.reload(); wallet.reload(); }}>Try again</Button></Section>;
  return <Resource resource={legal}>{documents => {
    if (documents.length !== 2 || !documents.some(document => document.type === "BusinessAgreement")
        || !documents.some(document => document.type === "AntiCircumventionAgreement"))
      return <Section title="Creation unavailable"><Notice error>Current Business agreements could not be verified.</Notice><Button variant="secondary" onClick={legal.reload}>Try again</Button></Section>;
    const outstanding = documents.filter(document => !document.accepted);
    if (outstanding.length) return <Section title="Current agreements required">
      <Notice error>Accept the current Business agreements before creating a Promotion or UGC.</Notice>
      <ul>{outstanding.map(document => <li key={document.id}>{document.type === "BusinessAgreement" ? "Business Agreement" : "Anti-Circumvention Agreement"} · {document.version}</li>)}</ul>
      <p className="fine-print">The current agreement text is not available in this workspace. Contact Weymela to review and accept the exact current versions.</p>
      <Button variant="secondary" onClick={() => { legal.reload(); wallet.reload(); }}>Check again</Button>
      <Link className="text-link" to="/business">Business Home</Link>
    </Section>;
    return <Resource resource={wallet}>{children}</Resource>;
  }}</Resource>;
}
