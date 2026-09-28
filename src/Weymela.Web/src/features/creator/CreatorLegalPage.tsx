import { useEffect, useState, type ReactNode } from "react";
import { Navigate, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import { Button, Notice, PageHeader, Resource } from "../../ui/components";

type LegalDocument = {
  id: string;
  type: string;
  version: string;
  contentHash: string;
  accepted: boolean;
};
type LegalContent = Pick<LegalDocument, "id" | "type" | "version" | "contentHash"> & { content: string };

const creatorDetail = /^\/creator\/(?:discover|promotions|campaigns)\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function creatorLegalReturn(value: string | null): string {
  if (value && (creatorDetail.test(value) || value === "/creator/discover"
      || value === "/creator/discover?tab=UGC" || value === "/creator/promotions"
      || /^\/creator\/promotions\?filter=(?:Active|Requests|Requested|History|Ended|DeclinedRejected)$/.test(value))) return value;
  return "/creator";
}
export function creatorLegalPath(value: string): string {
  return `/creator/legal?returnTo=${encodeURIComponent(creatorLegalReturn(value))}`;
}

function currentRules(documents: LegalDocument[]): LegalDocument | null {
  if (documents.some(document => document.type === "BusinessAgreement")) return null;
  const rules = documents.filter(document => document.type === "AntiCircumventionAgreement");
  return rules.length === 1 ? rules[0] : null;
}
function matchingContent(document: LegalDocument, content: LegalContent): boolean {
  return content.id === document.id && content.type === document.type
    && content.version === document.version && content.contentHash === document.contentHash
    && Boolean(content.content.trim());
}

export function CreatorLegalGate({ children }: { children: ReactNode }) {
  const location = useLocation();
  const legal = useResource<LegalDocument[]>("/legal/current");
  if (legal.loading) return <p className="loading" role="status">Checking current Creator requirements…</p>;
  if (legal.error) return <Notice error>{legal.error.message}</Notice>;
  return <Resource resource={legal}>{documents => {
    const rules = currentRules(documents);
    if (!rules) return <Notice error>Current Creator requirements could not be verified.</Notice>;
    return rules.accepted ? children : <Navigate to={creatorLegalPath(location.pathname + location.search)} replace />;
  }}</Resource>;
}

export function useCreatorLegalAction() {
  const navigate = useNavigate();
  return async (destination: string): Promise<boolean> => {
    const rules = currentRules(await request<LegalDocument[]>("/legal/current"));
    if (!rules) throw new Error("Current Creator requirements could not be verified.");
    if (rules.accepted) return true;
    navigate(creatorLegalPath(destination));
    return false;
  };
}

export function CreatorLegalPage() {
  const [params] = useSearchParams();
  const destination = creatorLegalReturn(params.get("returnTo"));
  const navigate = useNavigate();
  const legal = useResource<LegalDocument[]>("/legal/current");
  const action = useAction();
  const [acknowledged, setAcknowledged] = useState(false);
  const [content, setContent] = useState<LegalContent | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [viewing, setViewing] = useState(false);
  const rules = legal.data ? currentRules(legal.data) : null;
  const versionKey = rules ? `${rules.id}:${rules.version}:${rules.contentHash}` : "";

  useEffect(() => {
    setAcknowledged(false);
    setContent(null);
    setLoaded(false);
    setViewing(false);
    if (!rules || rules.accepted) return;
    let active = true;
    void request<LegalContent>(`/legal/${rules.id}/content`)
      .then(value => { if (active && matchingContent(rules, value)) setContent(value); })
      .catch(() => { /* The unavailable-content notice keeps acceptance blocked. */ })
      .finally(() => { if (active) setLoaded(true); });
    return () => { active = false; };
  }, [versionKey, rules?.accepted]);

  if (legal.loading) return <p className="loading" role="status">Checking current Creator requirements…</p>;
  if (legal.error) return <Notice error>{legal.error.message}</Notice>;
  if (!rules) return <Notice error>Current Creator requirements could not be verified.</Notice>;
  if (rules.accepted) return <Navigate to={destination} replace />;

  return <div className="creator-legal-page">
    <PageHeader title="Before you continue" />
    <div className="creator-legal-choice">
      <input id="creator-legal-acknowledgement" type="checkbox" aria-label="I agree to Weymela's rules and regulations."
        checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
      <div>
        <label htmlFor="creator-legal-acknowledgement">I agree to Weymela&apos;s </label>
        <button type="button" className="text-link" onClick={() => setViewing(true)}>rules and regulations</button>
        <span>.</span>
      </div>
    </div>
    {loaded && !content && <Notice error>Approved document text is unavailable. Acceptance is paused.</Notice>}
    {action.error && <Notice error>{action.error}</Notice>}
    <Button disabled={!acknowledged || !content || action.busy} onClick={() => void action.run(async () => {
      const latest = currentRules(await request<LegalDocument[]>("/legal/current"));
      if (!latest || latest.id !== rules.id || latest.version !== rules.version || latest.contentHash !== rules.contentHash)
        throw new Error("Creator requirements changed. Review the current version before accepting.");
      const verified = await request<LegalContent>(`/legal/${latest.id}/content`);
      if (!matchingContent(latest, verified)) throw new Error("Approved document text is unavailable. Acceptance is paused.");
      if (!latest.accepted) await post(`/legal/${latest.id}/accept`, { contentHash: latest.contentHash, confirmed: true });
      const accepted = currentRules(await request<LegalDocument[]>("/legal/current"));
      if (!accepted?.accepted || accepted.id !== latest.id || accepted.version !== latest.version
          || accepted.contentHash !== latest.contentHash)
        throw new Error("Creator requirements changed. Review the current version before continuing.");
      navigate(destination, { replace: true });
    })}>Accept &amp; Continue</Button>
    {viewing && <div className="creator-legal-overlay" role="presentation" onClick={() => setViewing(false)}>
      <section className="creator-legal-document" role="dialog" aria-modal="true" aria-label="Weymela rules and regulations"
        onClick={event => event.stopPropagation()}>
        <button type="button" className="text-link" onClick={() => setViewing(false)}>Back</button>
        <h2>Anti-Circumvention Rules</h2>
        <p className="muted">Version {rules.version}</p>
        {content ? <div className="creator-legal-copy">{content.content}</div>
          : <Notice error>Approved text for this version is unavailable.</Notice>}
      </section>
    </div>}
  </div>;
}
