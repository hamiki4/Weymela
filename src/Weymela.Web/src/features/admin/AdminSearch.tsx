import { useEffect, useState } from "react";
import { Button, Field } from "../../ui/components";

type Filters = { search: string; status: string; type: string; from: string; to: string; page: number };
export function useAdminSearch(path: string) {
  const [filters, setFilters] = useState<Filters>({ search: "", status: "", type: "", from: "", to: "", page: 1 });
  const [search, setSearch] = useState("");
  useEffect(() => {
    const timer = window.setTimeout(() => setSearch(filters.search.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [filters.search]);
  const params = new URLSearchParams();
  if (search) params.set("search", search);
  for (const key of ["status", "type", "from", "to"] as const) if (filters[key]) params.set(key, filters[key]);
  if (filters.page !== 1) params.set("page", String(filters.page));
  const update = (change: Partial<Filters>) => setFilters(current => ({ ...current, ...change, page: change.page ?? 1 }));
  return { filters, update, path: `${path}${params.size ? `${path.includes("?") ? "&" : "?"}${params}` : ""}` };
}
export function AdminSearch({ value, statuses = [], types = [], dates = false, label = "Search" }: {
  value: ReturnType<typeof useAdminSearch>; statuses?: string[]; types?: string[]; dates?: boolean; label?: string;
}) {
  const { filters, update } = value;
  return <div className="admin-search-controls">
    <Field label={label}><input type="search" maxLength={200} value={filters.search}
      onChange={event => update({ search: event.target.value })} placeholder={label === "Search business or deposit..." ? label : "Name, email, phone or reference"} /></Field>
    {statuses.length > 0 && <Field label="Status"><select value={filters.status} onChange={event => update({ status: event.target.value })}>
      <option value="">All statuses</option>{statuses.map(status => <option key={status}>{status}</option>)}
    </select></Field>}
    {types.length > 0 && <Field label="Type or method"><select value={filters.type} onChange={event => update({ type: event.target.value })}>
      <option value="">All</option>{types.map(type => <option key={type}>{type}</option>)}
    </select></Field>}
    {dates && <><Field label="From"><input type="date" value={filters.from} onChange={event => update({ from: event.target.value })} /></Field>
      <Field label="To"><input type="date" value={filters.to} min={filters.from} onChange={event => update({ to: event.target.value })} /></Field></>}
  </div>;
}
export function AdminPager({ value, count, loading = false }: { value: ReturnType<typeof useAdminSearch>; count: number; loading?: boolean }) {
  return <nav className="admin-pagination" aria-label="Results pages">
    <Button variant="secondary" disabled={loading || value.filters.page === 1} onClick={() => value.update({ page: value.filters.page - 1 })}>Previous</Button>
    <span>Page {value.filters.page}</span>
    <Button variant="secondary" disabled={loading || count < 50} onClick={() => value.update({ page: value.filters.page + 1 })}>Next</Button>
  </nav>;
}
