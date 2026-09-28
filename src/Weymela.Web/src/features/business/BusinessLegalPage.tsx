import { useEffect, useState } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import { Button, Notice, PageHeader, Resource } from "../../ui/components";

export type BusinessLegalDocument = {
  id: string;
  type: string;
  version: string;
  contentHash: string;
  accepted: boolean;
};
type LegalContent = Pick<BusinessLegalDocument, "id" | "type" | "version" | "contentHash"> & { content: string };

const destinations = new Set(["/business/campaigns/new", "/business/ugc/new"]);
export function businessLegalReturn(value: string | null): string {
  return value && destinations.has(value) ? value : "/business";
}
export function businessLegalPath(value: string): string {
  return `/business/legal?returnTo=${encodeURIComponent(businessLegalReturn(value))}`;
}

function currentBusinessDocuments(documents: BusinessLegalDocument[]): boolean {
  return documents.length === 2 && ["BusinessAgreement", "AntiCircumventionAgreement"].every(
    type => documents.filter(document => document.type === type).length === 1,
  );
}

export function BusinessLegalPage() {
  const [params] = useSearchParams();
  const destination = businessLegalReturn(params.get("returnTo"));
  const navigate = useNavigate();
  const legal = useResource<BusinessLegalDocument[]>("/legal/current");
  const action = useAction();
  const [acknowledged, setAcknowledged] = useState(false);
  const [content, setContent] = useState<Record<string, LegalContent> | null>(null);
  const [viewing, setViewing] = useState(false);
  const documents = legal.data;
  const versionKey = documents?.map(document => `${document.id}:${document.contentHash}`).join("|") ?? "";

  useEffect(() => {
    setAcknowledged(false);
    setContent(null);
    setViewing(false);
    if (!documents || !currentBusinessDocuments(documents) || documents.every(document => document.accepted)) return;
    let active = true;
    void Promise.all(documents.map(async document => {
      try {
        const value = await request<LegalContent>(`/legal/${document.id}/content`);
        return value.id === document.id && value.type === document.type && value.version === document.version
          && value.contentHash === document.contentHash && value.content.trim() ? value : null;
      } catch { return null; }
    })).then(values => {
      if (active) setContent(Object.fromEntries(values.filter((value): value is LegalContent => value !== null).map(value => [value.id, value])));
    });
    return () => { active = false; };
  }, [versionKey]);

  if (legal.loading) return <p className="loading" role="status">Checking current Business requirements…</p>;
  if (legal.error) return <Notice error>{legal.error.message}</Notice>;
  return <Resource resource={legal}>{current => {
    if (!currentBusinessDocuments(current)) return <Notice error>Current Business requirements could not be verified.</Notice>;
    if (current.every(document => document.accepted)) return <Navigate to={destination} replace />;
    const ready = current.every(document => Boolean(content?.[document.id]));
    return <div className="business-legal-page">
      <PageHeader title="Before you continue" />
      <div className="business-legal-choice">
        <input id="business-legal-acknowledgement" type="checkbox" aria-label="I agree to Weymela's rules and regulations." checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
        <div>
          <label htmlFor="business-legal-acknowledgement">I agree to Weymela&apos;s </label>
          <button type="button" className="text-link" onClick={() => setViewing(true)}>rules and regulations</button>
          <span>.</span>
        </div>
      </div>
      {!ready && content !== null && <Notice error>Approved document text is unavailable. Acceptance is paused.</Notice>}
      {action.error && <Notice error>{action.error}</Notice>}
      <Button disabled={!acknowledged || !ready || action.busy} onClick={() => void action.run(async () => {
        const latest = await request<BusinessLegalDocument[]>("/legal/current");
        if (!currentBusinessDocuments(latest) || latest.some(document => {
          const displayed = current.find(row => row.type === document.type);
          return displayed?.id !== document.id || displayed.contentHash !== document.contentHash;
        })) throw new Error("Business requirements changed. Review the current versions before accepting.");
        for (const document of latest.filter(row => !row.accepted))
          await post(`/legal/${document.id}/accept`, { contentHash: document.contentHash, confirmed: true });
        const accepted = await request<BusinessLegalDocument[]>("/legal/current");
        if (!currentBusinessDocuments(accepted) || accepted.some(row => !row.accepted || latest.find(document => document.type === row.type)?.id !== row.id))
          throw new Error("Business requirements changed. Review the current versions before continuing.");
        navigate(destination, { replace: true });
      })}>Accept &amp; Continue</Button>
      {viewing && <div className="business-legal-overlay" role="presentation" onClick={() => setViewing(false)}>
        <section className="business-legal-document" role="dialog" aria-modal="true" aria-label="Weymela rules and regulations" onClick={event => event.stopPropagation()}>
          <button type="button" className="text-link" onClick={() => setViewing(false)}>Back</button>
          {current.map(document => <div key={document.id}>
            <h2>{document.type === "BusinessAgreement" ? "Business Terms" : "Anti-Circumvention Rules"}</h2>
            <p className="muted">Version {document.version}</p>
            {content?.[document.id] ? <div className="business-legal-copy">{content[document.id].content}</div>
              : <Notice error>Approved text for this version is unavailable.</Notice>}
          </div>)}
        </section>
      </div>}
    </div>;
  }}</Resource>;
}
