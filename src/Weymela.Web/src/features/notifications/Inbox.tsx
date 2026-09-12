import { Link } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import { Button, Empty, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { date } from "../../ui/format";

interface Notification { id: string; title: string; message: string; route: string; createdAtUtc: string; readAtUtc: string | null }
interface InboxPage { items: Notification[]; unreadCount: number }
export function Inbox() {
  const resource = useResource<InboxPage>("/notifications"); const action = useAction();
  const read = (path: string) => void action.run(async key => { await post(path, undefined, key); resource.reload(); });
  return <><PageHeader eyebrow="Your workspace" title="Notifications" description="Updates that need your attention, all in one place." />
    {action.error && <Notice error>{action.error}</Notice>}
    <Resource resource={resource}>{page => <Section title={`${page.unreadCount} unread`} action={<Button variant="secondary" disabled={!page.unreadCount || action.busy} onClick={() => read("/notifications/read-all")}>Mark all read</Button>}>
      {!page.items.length ? <Empty icon="bell" title="You’re all caught up" message="Your Campaign and account updates will appear here." /> : <div className="notification-list">{page.items.map(item => <article className="notification-item" key={item.id}>
        <div><h3>{item.title}{!item.readAtUtc && <span className="notification-unread">Unread</span>}</h3><p>{item.message}</p><small>{date(item.createdAtUtc)}</small></div>
        <div className="actions"><Link className="text-link" to={item.route.startsWith("/") && !item.route.startsWith("//") ? item.route : "/"}>Open</Link>
          {!item.readAtUtc && <Button variant="quiet" disabled={action.busy} onClick={() => read(`/notifications/${item.id}/read`)}>Mark read</Button>}</div>
      </article>)}</div>}
    </Section>}</Resource></>;
}
