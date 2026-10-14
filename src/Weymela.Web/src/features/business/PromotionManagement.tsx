import { useSearchParams, Link } from "react-router-dom";
import { useResource } from "../../api/client";
import { ActionLink, Badge, Empty, PageHeader, Resource, Tabs } from "../../ui/components";
import { amount, date, promotionTypeLabel, promotionStatusLabel } from "../../ui/format";

export interface BusinessPromotionItem {
  key: string; id: string; campaignId: string; kind: string; title: string;
  group: "Pending" | "Active" | "Completed"; status: string; budget: number; remaining: number;
  dueAtUtc: string | null; primaryAction: string; actionPath: string; creator: string | null; remainingDays: number | null;
}
const groups = ["Pending", "Active", "Completed"] as const;

export function PromotionManagement() {
  const resource = useResource<BusinessPromotionItem[]>("/business/promotion-management");
  const [params, setParams] = useSearchParams();
  const requested = params.get("tab") ?? params.get("filter");
  const group = groups.find(value => value === requested)
    ?? (resource.data?.some(row => row.group === "Pending") ? "Pending" : "Active");
  return <>
    <PageHeader title="Promotions" compact action={<ActionLink to="/business/campaigns/new" icon="plus">Create Promotion</ActionLink>} />
    <div className="business-promotion-tabs"><Tabs label="Promotion management" value={group} onChange={value => setParams({ tab: value })}
      items={groups.map(value => ({ value, label: `${value} (${resource.data?.filter(row => row.group === value).length ?? 0})` }))} /></div>
    <Resource resource={resource}>{rows => {
      const visible = rows.filter(row => row.group === group);
      return visible.length ? <div className="card-stack business-promotion-list">
        {visible.map(row => <article className="business-promotion-card campaign-card" key={row.key}
          data-notification-resource={row.kind === "Creator application" || row.kind === "Creator work" || row.kind === "UGC deliverable" ? row.id : undefined}>
          <div className="card-head"><div><p className="card-eyebrow">{["ViewOnly", "ViewPlusCommission"].includes(row.kind) ? promotionTypeLabel(row.kind) : row.kind}</p>
            <h3>{row.title}</h3>{row.creator && <p>{row.creator}</p>}</div><Badge status={row.status} label={promotionStatusLabel(row.status)} /></div>
          <dl className="promotion-summary-list">
            {row.budget > 0 && <><div><dt>{row.kind === "UGC deliverable" ? "Agreed payment" : "Budget"}</dt><dd>{amount(row.budget)} ETB</dd></div>
              <div><dt>Remaining</dt><dd>{amount(row.remaining)} ETB</dd></div></>}
            {row.dueAtUtc && <div><dt>Due</dt><dd>{date(row.dueAtUtc)}</dd></div>}
            {row.remainingDays != null && <div><dt>Remaining duration</dt><dd>{row.remainingDays} days</dd></div>}
          </dl>
          <div className="actions"><Link className="button secondary business-promotion-manage" to={row.actionPath}>{row.primaryAction}</Link>
            {row.kind === "UGC campaign" && row.group !== "Completed" && <Link className="text-link" to={`/business/ugc?edit=${row.campaignId}`}>Edit details</Link>}</div>
        </article>)}
      </div> : <Empty title={`No ${group.toLowerCase()} promotions`} />;
    }}</Resource>
  </>;
}
