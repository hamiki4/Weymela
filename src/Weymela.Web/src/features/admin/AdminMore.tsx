import { Link, useLocation } from "react-router-dom";
import { useSession } from "../../app/Session";
import { Icon } from "../../ui/Icon";
import { PageHeader } from "../../ui/components";

type MoreLink = [string, string];

export function AdminMore() {
  const { user } = useSession();
  const location = useLocation();
  const platform = user?.role === "PlatformAdmin";
  const home = platform ? "/admin" : "/admin/operations";
  const from = (location.state as { from?: unknown } | null)?.from;
  const back = typeof from === "string" && from.startsWith("/admin") && from !== "/admin/more" ? from : home;
  const groups: [string, MoreLink[]][] = platform ? [
    ["Accounts", [["Customers", "/admin/customers"], ["Creators", "/admin/creators"],
      ["Businesses", "/admin/businesses"], ["Admins", "/admin/admins"]]],
    ["Promotions", [["Campaigns", "/admin/campaigns"], ["UGC", "/admin/ugc"]]],
    ["Management", [["Reports", "/admin/reports"], ["Financial Settings", "/admin/settings"],
      ["Notifications", "/notifications"]]],
  ] : [
    ["Accounts", [["Profile Requests", "/admin/role-enrollments"],
      ["Customers", "/admin/operations/customers"], ["Creators", "/admin/operations/creators"],
      ["Businesses", "/admin/operations/businesses"]]],
    ["Promotions", [["Campaigns", "/admin/campaigns"], ["UGC", "/admin/ugc"]]],
    ["Management", [["Notifications", "/notifications"]]],
  ];
  return <div className="admin-more-page">
    <Link className="admin-more-back" to={back}><Icon name="back" size={18} />Back</Link>
    <PageHeader title="More" />
    {groups.map(([title, links]) => <section className="admin-more-group" key={title}>
      <h2>{title}</h2>
      <div>{links.map(([label, path]) => <Link key={path} to={path}>
        <span>{label}</span><Icon name="arrow" size={17} />
      </Link>)}</div>
    </section>)}
  </div>;
}
