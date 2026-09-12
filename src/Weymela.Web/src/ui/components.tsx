import {
  useEffect,
  useId,
  useRef,
  useState,
  cloneElement,
  isValidElement,
  type ReactElement,
  type ButtonHTMLAttributes,
  type ReactNode,
} from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/client";
import type { Activity, CreatorCard } from "../api/types";
import { amount, count, dateTime, safeExternal, statusLabel } from "./format";
import { Icon } from "./Icon";

export function Button({
  children,
  variant = "primary",
  icon,
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "quiet";
  icon?: string;
}) {
  return (
    <button
      className={`button ${variant} ${className}`}
      type="button"
      {...props}
    >
      {icon && <Icon name={icon} />}
      {children}
    </button>
  );
}
export function ActionLink({
  to,
  children,
  icon = "arrow",
  secondary = false,
}: {
  to: string;
  children: ReactNode;
  icon?: string;
  secondary?: boolean;
}) {
  return (
    <Link className={`button ${secondary ? "secondary" : "primary"}`} to={to}>
      {children}
      <Icon name={icon} />
    </Link>
  );
}
export function PageHeader({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <header className="page-heading">
      <div>
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1>{title}</h1>
        {description && <p className="lead">{description}</p>}
      </div>
      {action && <div className="heading-action">{action}</div>}
    </header>
  );
}
export function Section({
  title,
  description,
  action,
  children,
  className = "",
}: {
  title: string;
  description?: string;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`panel ${className}`}>
      <div className="section-heading">
        <div>
          <h2>{title}</h2>
          {description && <p>{description}</p>}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}
export function Metric({
  label,
  value,
  note,
  icon = "chart",
  emphasis = false,
}: {
  label: string;
  value: ReactNode;
  note?: string;
  icon?: string;
  emphasis?: boolean;
}) {
  return (
    <article className={`metric ${emphasis ? "emphasis" : ""}`}>
      <div className="metric-top">
        <span>{label}</span>
        <Icon name={icon} />
      </div>
      <strong>{value}</strong>
      {note && <small>{note}</small>}
    </article>
  );
}
export function Currency() {
  return <span className="currency">ETB</span>;
}
export function Badge({ status }: { status: string }) {
  return (
    <span
      className={`badge ${["Active", "Approved", "Paid", "Eligible", "Ready"].includes(status) ? "positive" : ["FundingRequired", "BudgetExhausted", "Rejected", "Inactive"].includes(status) ? "attention" : ""}`}
    >
      {statusLabel(status)}
    </span>
  );
}
export function Empty({
  title,
  message,
  action,
  icon = "campaign",
}: {
  title: string;
  message: string;
  action?: ReactNode;
  icon?: string;
}) {
  return (
    <div className="empty-state">
      <span className="empty-icon">
        <Icon name={icon} size={28} />
      </span>
      <h3>{title}</h3>
      <p>{message}</p>
      {action}
    </div>
  );
}
export function Notice({
  children,
  error = false,
}: {
  children: ReactNode;
  error?: boolean;
}) {
  return (
    <div
      className={`notice ${error ? "error" : ""}`}
      role={error ? "alert" : "status"}
    >
      <Icon name={error ? "info" : "check"} />
      <div>{children}</div>
    </div>
  );
}
export function Resource<T>({
  resource,
  children,
}: {
  resource: {
    data: T | null;
    loading: boolean;
    error: Error | null;
    reload: () => void;
  };
  children: (data: T) => ReactNode;
}) {
  if (resource.loading && resource.data === null)
    return (
      <div className="loading" role="status" aria-label="Loading workspace">
        <div className="skeleton skeleton-title" />
        <div className="metric-grid">
          <div className="skeleton skeleton-card" />
          <div className="skeleton skeleton-card" />
          <div className="skeleton skeleton-card" />
        </div>
        <span>Loading your workspace…</span>
      </div>
    );
  if (resource.error)
    return (
      <Section
        title={
          resource.error instanceof ApiError && resource.error.status === 403
            ? "Workspace unavailable"
            : "Let’s try that again"
        }
      >
        <Empty
          icon="lock"
          title={
            resource.error instanceof ApiError && resource.error.status === 401
              ? "Sign in to continue"
              : "We couldn’t open this page"
          }
          message={resource.error.message}
          action={
            resource.error instanceof ApiError &&
            resource.error.status === 401 ? (
              <ActionLink to="/sign-in">Sign in</ActionLink>
            ) : (
              <Button onClick={resource.reload}>Try again</Button>
            )
          }
        />
      </Section>
    );
  return resource.data === null ? null : (
    <div aria-busy={resource.loading}>{children(resource.data)}</div>
  );
}
export function Field({
  label,
  help,
  children,
  wide = false,
}: {
  label: string;
  help?: string;
  children: ReactNode;
  wide?: boolean;
}) {
  const id = useId();
  const control = isValidElement(children)
    ? cloneElement(
        children as ReactElement<{ id?: string; "aria-describedby"?: string }>,
        { id, "aria-describedby": help ? `${id}-help` : undefined },
      )
    : children;
  return (
    <div className={`field ${wide ? "wide" : ""}`}>
      <label htmlFor={id}>{label}</label>
      {control}
      {help && <small id={`${id}-help`}>{help}</small>}
    </div>
  );
}
export function MoneyInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      type="number"
      min="0.01"
      step="0.01"
      inputMode="decimal"
      required
      {...props}
    />
  );
}
export function Tabs({
  items,
  value,
  onChange,
  label,
}: {
  items: { value: string; label: string }[];
  value: string;
  onChange: (value: string) => void;
  label: string;
}) {
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  return (
    <div className="tabs" role="tablist" aria-label={label}>
      {items.map((item, index) => (
        <button
          key={item.value}
          ref={(el) => {
            refs.current[index] = el;
          }}
          type="button"
          role="tab"
          aria-selected={value === item.value}
          tabIndex={value === item.value ? 0 : -1}
          onClick={() => onChange(item.value)}
          onKeyDown={(event) => {
            let next = index;
            if (event.key === "ArrowRight") next = (index + 1) % items.length;
            else if (event.key === "ArrowLeft")
              next = (index - 1 + items.length) % items.length;
            else if (event.key === "Home") next = 0;
            else if (event.key === "End") next = items.length - 1;
            else return;
            event.preventDefault();
            onChange(items[next].value);
            refs.current[next]?.focus();
          }}
        >
          {item.label}
        </button>
      ))}
    </div>
  );
}
export function Dialog({
  title,
  open,
  onClose,
  children,
}: {
  title: string;
  open: boolean;
  onClose: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const id = useId();
  useEffect(() => {
    if (open && !ref.current?.open) ref.current?.showModal();
    if (!open && ref.current?.open) ref.current?.close();
  }, [open]);
  return (
    <dialog
      className="dialog"
      ref={ref}
      aria-labelledby={id}
      onClose={onClose}
      onCancel={onClose}
    >
      <div className="dialog-header">
        <h2 id={id}>{title}</h2>
        <Button variant="quiet" aria-label="Close dialog" onClick={onClose}>
          <Icon name="close" />
        </Button>
      </div>
      {children}
    </dialog>
  );
}
export function Person({
  person,
  detail = false,
}: {
  person: CreatorCard;
  detail?: boolean;
}) {
  return (
    <div className="person">
      <span className="avatar" aria-hidden="true">
        {person.displayName.slice(0, 1)}
      </span>
      <div>
        <strong>{person.displayName}</strong>
        <small>
          {person.publicId}
          {detail && ` · ${person.category}`}
        </small>
      </div>
    </div>
  );
}
export function CreatorProfile({ person }: { person: CreatorCard }) {
  return (
    <div className="profile">
      <Person person={person} detail />
      <div className="mini-metrics">
        <div>
          <span>Verified followers</span>
          <strong>{count(person.verifiedFollowers)}</strong>
        </div>
        <div>
          <span>Verified views</span>
          <strong>{count(person.verifiedViews)}</strong>
        </div>
      </div>
      <p>
        <Icon name="location" />
        {person.region}
      </p>
      {safeExternal(person.portfolioUrl) ? (
        <a
          className="button secondary"
          href={safeExternal(person.portfolioUrl)}
          target="_blank"
          rel="noreferrer"
        >
          Open portfolio
          <Icon name="arrow" />
        </a>
      ) : (
        <p className="muted">No approved portfolio samples yet.</p>
      )}
    </div>
  );
}
export function ActivityList({ items }: { items: Activity[] }) {
  const [expanded, setExpanded] = useState(false);
  return items.length ? (
    <>
    <ol className="activity-list">
      {(expanded ? items : items.slice(0, 6)).map((item) => (
        <li key={item.id}>
          <span className="activity-dot" />
          <div>
            <strong>{item.title}</strong>
            <small>{dateTime(item.atUtc)}</small>
            <details>
              <summary>Reference</summary>
              <code>{item.reference}</code>
            </details>
          </div>
        </li>
      ))}
    </ol>
    {items.length > 6 && <Button variant="secondary" aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>{expanded ? 'Show recent activity' : `Show all activity (${items.length})`}</Button>}
    </>
  ) : (
    <Empty
      title="No activity yet"
      message="Recorded actions will appear here."
      icon="document"
    />
  );
}
export function FundsGrid({ values }: { values: [string, number][] }) {
  return (
    <dl className="funds-grid">
      {values.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{amount(value)}</dd>
        </div>
      ))}
    </dl>
  );
}
export interface Column<T> {
  label: string;
  cell: (row: T) => ReactNode;
  numeric?: boolean;
}
export function DataTable<T>({
  rows,
  columns,
  rowKey,
  label,
  card,
  empty,
}: {
  rows: T[];
  columns: Column<T>[];
  rowKey: (row: T) => string;
  label: string;
  card: (row: T) => ReactNode;
  empty: ReactNode;
}) {
  if (!rows.length) return <>{empty}</>;
  return (
    <>
      <div className="desktop-data">
        <table>
          <caption className="sr-only">{label}</caption>
          <thead>
            <tr>
              {columns.map((c) => (
                <th
                  key={c.label}
                  className={c.numeric ? "numeric" : ""}
                  scope="col"
                >
                  {c.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={rowKey(row)}>
                {columns.map((c) => (
                  <td key={c.label} className={c.numeric ? "numeric" : ""}>
                    {c.cell(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="mobile-data card-stack">
        {rows.map((row) => (
          <article className="data-card" key={rowKey(row)}>
            {card(row)}
          </article>
        ))}
      </div>
    </>
  );
}
