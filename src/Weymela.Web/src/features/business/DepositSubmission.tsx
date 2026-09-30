import { useEffect, useRef, useState } from "react";
import { ApiError, postForm, useAction, useResource } from "../../api/client";
import { Button, Field, MoneyInput, Notice, Resource } from "../../ui/components";
import { amount, date } from "../../ui/format";
interface Receipt { id: string; amount: number; status: string; submittedAtUtc: string }
export function DepositSubmission() {
  const method = useResource<{ mode: string }>("/business/deposit-method");
  return <Resource resource={method}>{data => data.mode === "ManualApproval" ? <ManualDeposit /> : <Notice>Deposits are not connected. No payment will be taken or funds credited.</Notice>}</Resource>;
}
export function ManualDeposit() {
  const [value, setValue] = useState(""); const [receipt, setReceipt] = useState<File | null>(null);
  const [fileError, setFileError] = useState("");
  const [preview, setPreview] = useState(""); const [submitted, setSubmitted] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const action = useAction(); const history = useResource<Receipt[]>("/business/deposit-requests");
  useEffect(() => {
    if (!receipt || typeof URL.createObjectURL !== "function") { setPreview(""); return; }
    const url = URL.createObjectURL(receipt); setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [receipt]);
  const remove = () => { setFileError(""); setReceipt(null); if (input.current) input.current.value = ""; };
  return <>
    <form onSubmit={event => { event.preventDefault(); if (!receipt) return; void action.run(async key => {
      if (receipt.size > 4 * 1024 * 1024) throw new Error("Receipt must be 4 MB or smaller.");
      const form = new FormData(); form.set("amount", value); form.set("receipt", receipt);
      try { await postForm("/business/deposit-requests", form, key); }
      catch (error) {
        if (error instanceof ApiError && error.status === 413)
          throw new Error("Receipt must be 4 MB or smaller.");
        if (error instanceof ApiError && error.code === "FinancialWritesPaused")
          throw new Error("Adding funds is temporarily paused. Your receipt was not submitted.");
        throw error;
      }
      setSubmitted(true); setValue(""); remove(); history.reload();
    }); }}><fieldset disabled={action.busy}>
      <Field label="Amount"><MoneyInput value={value} onChange={event => { setValue(event.target.value); setSubmitted(false); }} /></Field>
      <Field label="Payment receipt"><input ref={input} type="file" accept="image/jpeg,image/png" aria-label="Payment receipt" className="receipt-file-input" onChange={event => { const file = event.target.files?.[0] ?? null; setSubmitted(false);
        if (file && file.size > 4 * 1024 * 1024) { remove(); setFileError("Receipt must be 4 MB or smaller."); return; }
        setFileError(""); setReceipt(file); }} /><Button type="button" variant="secondary" onClick={() => input.current?.click()}>Upload Receipt</Button></Field>
      {receipt && <div className="receipt-selection"><div className="receipt-thumbnail">{preview && <img src={preview} alt="Selected payment receipt" />}</div><div><span className="receipt-name">{receipt.name}</span><div className="actions"><Button type="button" variant="secondary" onClick={() => input.current?.click()}>Replace</Button><Button type="button" variant="secondary" onClick={remove}>Remove</Button></div></div></div>}
      {fileError && <Notice error>{fileError}</Notice>}{action.error && <Notice error>{action.error}</Notice>}{submitted && <Notice><strong>Under review</strong><br />Your payment is waiting for approval.</Notice>}
      <Button type="submit" disabled={!value || !receipt || action.busy}>{action.busy ? "Submitting…" : "Submit for Review"}</Button>
    </fieldset></form>
    <Resource resource={history}>{rows => <>{rows.map(row => <div className="amount-row" key={row.id}><div><strong>{row.status === "Pending" ? "Under review" : row.status === "Approved" ? "Approved" : "Rejected"}</strong><small>{date(row.submittedAtUtc)}</small></div><strong>{amount(row.amount)}</strong></div>)}</>}</Resource>
  </>;
}
