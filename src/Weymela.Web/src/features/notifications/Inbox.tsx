import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { notifyNotificationsChanged, post, useAction, useResource } from "../../api/client";
import { Button, Empty, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { dateTime } from "../../ui/format";

interface Notification { id: string; title: string; message: string; route: string; createdAtUtc: string; readAtUtc: string | null }
interface InboxPage { items: Notification[]; unreadCount: number }
export function Inbox() {
  const resource = useResource<InboxPage>("/notifications"); const action = useAction();
  const navigate = useNavigate();
  const [readIds,setReadIds] = useState<Set<string>>(() => new Set());
  const read = (path: string) => void action.run(async key => { await post(path, undefined, key); notifyNotificationsChanged(); resource.reload(); });
  return <><PageHeader title="Notifications" compact />
    {action.error && <Notice error>{action.error}</Notice>}
    <Resource resource={resource}>{page => <Section title={`${page.unreadCount} unread`} action={<Button variant="secondary" disabled={!page.unreadCount || action.busy} onClick={() => read("/notifications/read-all")}>Mark all read</Button>}>
      {!page.items.length ? <Empty icon="bell" title="You’re all caught up" message="Your Campaign and account updates will appear here." /> : <div className="notification-list">{page.items.map(item => <article className="notification-item compact-notification" data-unread={!item.readAtUtc && !readIds.has(item.id)} key={item.id}>
        <Link className="notification-row" data-notification-open aria-label={item.title} to={item.route.startsWith("/") && !item.route.startsWith("//") ? item.route : "/"}
          onClick={event => {
            if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) { if (!item.readAtUtc) read(`/notifications/${item.id}/read`); return; }
            event.preventDefault();
            const profileKey = sessionStorage.getItem("weymela.profile-key");
            const sameProfile = () => sessionStorage.getItem("weymela.profile-key") === profileKey;
            void action.run(async key => {
              if (!item.readAtUtc && !readIds.has(item.id)) {
                setReadIds(current => new Set(current).add(item.id)); notifyNotificationsChanged(-1);
                try { await post(`/notifications/${item.id}/read`, undefined, key); if (sameProfile()) notifyNotificationsChanged(); }
                catch (error) { setReadIds(current => { const next = new Set(current); next.delete(item.id); return next; }); if (sameProfile()) notifyNotificationsChanged(1); throw error; }
              }
              if (!sameProfile()) return;
              navigate(item.route.startsWith("/") && !item.route.startsWith("//") ? item.route : "/");
            });
          }}><span>{item.title}</span><time dateTime={item.createdAtUtc}>{dateTime(item.createdAtUtc)}</time><span className="notification-state" aria-label={item.readAtUtc || readIds.has(item.id) ? "Read" : "Unread"}>{item.readAtUtc || readIds.has(item.id) ? "✓" : "•"}</span></Link>
      </article>)}</div>}
    </Section>}</Resource></>;
}
