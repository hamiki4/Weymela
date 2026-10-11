import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button, Notice } from "../ui/components";

type LegalDocumentKind = "terms" | "privacy";

const documents: Record<
  LegalDocumentKind,
  { title: string; source: string }
> = {
  terms: {
    title: "Terms of Service",
    source: "/legal/pilot-terms-of-service-v1.txt",
  },
  privacy: {
    title: "Privacy Policy",
    source: "/legal/pilot-privacy-policy-v1.txt",
  },
};

export function LegalDocumentPage({ kind }: { kind: LegalDocumentKind }) {
  const document = documents[kind];
  const production = import.meta.env.VITE_FIREBASE_PROJECT_ID === "weymela-production";
  const [language, setLanguage] = useState<"en" | "am">("en");
  const source = production ? `/api/legal/account/${kind}?language=${language}` : document.source;
  const [content, setContent] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [generation, setGeneration] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setContent(null);
    setFailed(false);
    void fetch(source, {
      credentials: "same-origin",
      cache: "no-store",
      signal: controller.signal,
    })
      .then((response) => {
        if (!response.ok) throw new Error("Legal document unavailable");
        return production ? response.json().then((value: { content: string }) => value.content) : response.text();
      })
      .then(setContent)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === "AbortError"))
          setFailed(true);
      });
    return () => controller.abort();
  }, [source, production, generation]);

  return (
    <main className="legal-document-page">
      <article className="legal-document-card" aria-labelledby="legal-title">
        <Link className="auth-secondary-action legal-back" to="/onboarding">
          Back
        </Link>
        <header>
          <p className="eyebrow">{production ? "Weymela" : "Weymela Pilot"}</p>
          <h1 id="legal-title">{document.title}</h1>
        </header>
        {production && <div><Button variant="secondary" onClick={() => setLanguage("en")}>English</Button>
          <Button variant="secondary" onClick={() => setLanguage("am")}>አማርኛ</Button></div>}
        {!content && !failed && (
          <div className="loading" role="status">
            Opening document…
          </div>
        )}
        {failed && (
          <div className="legal-document-error">
            <Notice error>We couldn't open this document.</Notice>
            <Button variant="secondary" onClick={() => setGeneration((value) => value + 1)}>
              Try again
            </Button>
          </div>
        )}
        {content && <div className="legal-document-copy">{content}</div>}
      </article>
    </main>
  );
}
