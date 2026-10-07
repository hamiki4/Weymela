import { useEffect, useRef, useState } from "react";
import { ApiError, postForm, useAction, useResource } from "../../api/client";
import { Button, Field, MoneyInput, Notice, Resource } from "../../ui/components";
import { amount, date } from "../../ui/format";
export interface DepositRequest { id: string; amount: number; status: string; submittedAtUtc: string; destinationName?: string | null; destinationAccount?: string | null }
interface ReceivingDestination { id: string; method: string; name: string; accountReference: string }
export function DepositSubmission({ showHistory = true, onSubmitted }: { showHistory?: boolean; onSubmitted?: () => void } = {}) {
  const method = useResource<{ mode: string }>("/business/deposit-method");
  return <Resource resource={method}>{data => data.mode === "ManualApproval" ? <ManualDeposit showHistory={showHistory} onSubmitted={onSubmitted} /> : <Notice>Deposits are not connected. No payment will be taken or funds credited.</Notice>}</Resource>;
}
export function ManualDeposit({ showHistory = true, onSubmitted }: { showHistory?: boolean; onSubmitted?: () => void } = {}) {
  const [value, setValue] = useState(""); const [destination, setDestination] = useState(""); const [receipt, setReceipt] = useState<File | null>(null);
  const [fileError, setFileError] = useState("");
  const [preview, setPreview] = useState(""); const [submitted, setSubmitted] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const action = useAction(); const history = useResource<DepositRequest[]>("/business/deposit-requests");
  const destinations = useResource<ReceivingDestination[]>("/business/receiving-destinations");
  useEffect(() => { if (!destination && destinations.data?.[0]) setDestination(destinations.data[0].id); }, [destination, destinations.data]);
  useEffect(() => {
    if (!receipt || typeof URL.createObjectURL !== "function") { setPreview(""); return; }
    const url = URL.createObjectURL(receipt); setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [receipt]);
  const remove = () => { setFileError(""); setReceipt(null); if (input.current) input.current.value = ""; };
  return <>
    <Resource resource={destinations}>{rows => <form onSubmit={event => { event.preventDefault(); if (!receipt || !destination) return; void action.run(async key => {
      if (receipt.size > 4 * 1024 * 1024) throw new Error("Receipt must be 4 MB or smaller.");
      const form = new FormData(); form.set("amount", value); form.set("receivingDestinationId", destination); form.set("receipt", receipt);
      try { await postForm("/business/deposit-requests", form, key); }
      catch (error) {
        if (error instanceof ApiError && error.status === 413)
          throw new Error("Receipt must be 4 MB or smaller.");
        if (error instanceof ApiError && error.code === "FinancialWritesPaused")
          throw new Error("Adding funds is temporarily paused. Your receipt was not submitted.");
        throw error;
      }
      setSubmitted(true); setValue(""); remove(); history.reload(); onSubmitted?.();
    }); }}><fieldset disabled={action.busy}>
      <Field label="Deposited to"><select required value={destination} onChange={event => { setDestination(event.target.value); setSubmitted(false); }}><option value="">Choose destination</option>{rows.map(row => <option key={row.id} value={row.id}>{row.name} · {row.accountReference}</option>)}</select></Field>
      <Field label="Amount"><MoneyInput value={value} onChange={event => { setValue(event.target.value); setSubmitted(false); }} /></Field>
      <Field label="Payment receipt"><input ref={input} type="file" accept="image/jpeg,image/png,application/pdf" aria-label="Payment receipt" className="receipt-file-input" onChange={event => { const file = event.target.files?.[0] ?? null; setSubmitted(false);
        if (file && file.size > 4 * 1024 * 1024) { remove(); setFileError("Receipt must be 4 MB or smaller."); return; }
        setFileError(""); setReceipt(file); }} /><Button type="button" variant="secondary" onClick={() => input.current?.click()}>Upload Receipt</Button></Field>
      {receipt && <div className="receipt-selection"><div className="receipt-thumbnail">{preview && receipt.type !== "application/pdf" && <img src={preview} alt="Selected payment receipt" />}{receipt.type === "application/pdf" && <span aria-label="Selected PDF receipt">PDF</span>}</div><div><span className="receipt-name">{receipt.name}</span><div className="actions"><Button type="button" variant="secondary" onClick={() => input.current?.click()}>Replace</Button><Button type="button" variant="secondary" onClick={remove}>Remove</Button></div></div></div>}
      {fileError && <Notice error>{fileError}</Notice>}{action.error && <Notice error>{action.error}</Notice>}{submitted && <Notice><strong>Under review</strong><br />Your payment is waiting for approval.</Notice>}
      <Button type="submit" disabled={!value || !destination || !receipt || action.busy}>{action.busy ? "Submitting…" : "Submit for Review"}</Button>
    </fieldset></form>}</Resource>
    {showHistory && <Resource resource={history}>{rows => <>{rows.map(row => <div className="amount-row" key={row.id}><div><strong>{row.status === "Pending" ? "Under review" : row.status === "Approved" ? "Approved" : "Rejected"}</strong><small>{row.destinationName ? `${row.destinationName} · ${row.destinationAccount} · ` : ""}{date(row.submittedAtUtc)}</small></div><strong>{amount(row.amount)}</strong></div>)}</>}</Resource>}
  </>;
}
