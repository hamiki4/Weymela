import { Link, useNavigate } from "react-router-dom";
import { notifyNotificationsChanged, post, useAction, useResource } from "../../api/client";
import { Button, Empty, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { date } from "../../ui/format";

interface Notification { id: string; title: string; message: string; route: string; createdAtUtc: string; readAtUtc: string | null }
interface InboxPage { items: Notification[]; unreadCount: number }
export function Inbox() {
  const resource = useResource<InboxPage>("/notifications"); const action = useAction();
  const navigate = useNavigate();
  const read = (path: string) => void action.run(async key => { await post(path, undefined, key); notifyNotificationsChanged(); resource.reload(); });
  return <><PageHeader title="Notifications" compact />
    {action.error && <Notice error>{action.error}</Notice>}
    <Resource resource={resource}>{page => <Section title={`${page.unreadCount} unread`} action={<Button variant="secondary" disabled={!page.unreadCount || action.busy} onClick={() => read("/notifications/read-all")}>Mark all read</Button>}>
      {!page.items.length ? <Empty icon="bell" title="You’re all caught up" message="Your Campaign and account updates will appear here." /> : <div className="notification-list">{page.items.map(item => <article className="notification-item" data-unread={!item.readAtUtc} key={item.id}>
        <div><h3>{item.title}{!item.readAtUtc && <span className="notification-unread">Unread</span>}</h3><p>{item.message}</p><small>{date(item.createdAtUtc)}</small></div>
        <div className="actions"><Link className="text-link" data-notification-open to={item.route.startsWith("/") && !item.route.startsWith("//") ? item.route : "/"}
          onClick={event => {
            if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) { if (!item.readAtUtc) read(`/notifications/${item.id}/read`); return; }
            event.preventDefault();
            void action.run(async key => {
              if (!item.readAtUtc) { await post(`/notifications/${item.id}/read`, undefined, key); notifyNotificationsChanged(); }
              navigate(item.route.startsWith("/") && !item.route.startsWith("//") ? item.route : "/");
            });
          }}>Open</Link></div>
      </article>)}</div>}
    </Section>}</Resource></>;
}
