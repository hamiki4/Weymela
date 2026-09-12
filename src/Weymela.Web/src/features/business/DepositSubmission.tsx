import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import { Button, Field, MoneyInput, Notice, Resource } from "../../ui/components";
import { amount, date } from "../../ui/format";
interface Receipt { id: string; amount: number; status: string; submittedAtUtc: string }
export function DepositSubmission() {
  const method = useResource<{ mode: string }>("/business/deposit-method");
  return <Resource resource={method}>{data => data.mode === "ManualApproval" ? <ManualDeposit /> : <Notice>Deposits are not connected. No payment will be taken or funds credited.</Notice>}</Resource>;
}
export function ManualDeposit() {
  const [value, setValue] = useState(""); const [reference, setReference] = useState(""); const [submitted, setSubmitted] = useState(false);
  const action = useAction(); const history = useResource<Receipt[]>("/business/deposit-requests");
  return <><p className="fine-print">Submit your completed payment reference for Admin review. Funds become available only after receipt is confirmed.</p>
    <form onSubmit={event => { event.preventDefault(); void action.run(async key => {
      await post("/business/deposit-requests", { amount: Number(value), externalReference: reference, proofReference: null }, key);
      setSubmitted(true); setValue(""); setReference(""); history.reload();
    }); }}><fieldset disabled={action.busy}>
      <Field label="Amount (ETB)"><MoneyInput value={value} onChange={event => { setValue(event.target.value); setSubmitted(false); }} /></Field>
      <Field label="Payment reference" help="Use the reference from your completed payment. Do not enter passwords or private payment details."><input required maxLength={120} pattern="[A-Za-z0-9._-]+" value={reference} onChange={event => setReference(event.target.value)} /></Field>
      {action.error && <Notice error>{action.error}</Notice>}{submitted && <Notice>Deposit submitted for review. Your wallet has not been credited yet.</Notice>}
      <Button type="submit" disabled={!value || !reference || action.busy}>{action.busy ? "Submitting…" : "Submit for Review"}</Button>
    </fieldset></form>
    <Resource resource={history}>{rows => <>{rows.map(row => <div className="amount-row" key={row.id}><div><strong>{row.status === "Pending" ? "Awaiting review" : row.status === "Approved" ? "Approved" : "Not approved"}</strong><small>{date(row.submittedAtUtc)}</small></div><strong>{amount(row.amount)}</strong></div>)}</>}</Resource>
  </>;
}
