import { useEffect, useState } from "react";
import { post, useAction, useResource } from "../api/client";
import type { PayoutDestination } from "../api/types";
import { Button, Field, Notice, Resource } from "../ui/components";

export function PayoutDestinationForm({ endpoint }: { endpoint: string }) {
  const resource = useResource<PayoutDestination>(endpoint);
  const action = useAction();
  const [method, setMethod] = useState("Telebirr");
  const [bankName, setBankName] = useState("");
  const [accountNumber, setAccountNumber] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    if (!resource.data) return;
    setMethod(resource.data.method);
    setBankName(resource.data.method === "Bank" ? resource.data.provider : "");
  }, [resource.data]);
  return <Resource resource={resource}>{current => <form className="payout-destination-form" onSubmit={event => { event.preventDefault(); void action.run(async key => {
    await post(endpoint, method !== "Bank" ? { method, bankName: null, accountNumber: null } : { method, bankName: bankName.trim(), accountNumber: accountNumber.trim() }, key);
    setAccountNumber(""); setSaved(true); resource.reload();
  }); }}><fieldset disabled={action.busy}>
    <fieldset className="payout-methods"><legend>Payout method</legend>{[["Telebirr", "Telebirr"], ["Mpesa", "M-PESA"], ["Bank", "Bank account"]].map(([value, label]) => <label key={value}><input type="radio" name="payout-method" value={value} checked={method === value} onChange={() => { setMethod(value); setSaved(false); }} />{label}</label>)}</fieldset>
    {method !== "Bank" ? <>
      <Field label="Verified Weymela phone"><input value={current.registeredPhone ?? (current.method !== "Bank" ? current.account : "")} placeholder="No verified phone available" readOnly /></Field>
      {method === "Mpesa" && <p className="fine-print">Use your registered Safaricom Ethiopia number. M-PESA wallet ownership is checked before payment.</p>}
    </> : <>
      <Field label="Bank Name"><input required maxLength={100} value={bankName} onChange={event => { setBankName(event.target.value); setSaved(false); }} /></Field>
      {current.isConfigured && current.method === "Bank" && <p className="fine-print">Current account: {current.account}</p>}
      <Field label="Account Number"><input required inputMode="numeric" pattern="[0-9 ]{6,34}" maxLength={34} value={accountNumber} onChange={event => { setAccountNumber(event.target.value); setSaved(false); }} /></Field>
    </>}
    <p className="fine-print">Recipient: <span data-no-translate>{current.legalName}</span></p>
    {action.error && <Notice error>{action.error}</Notice>}{saved && <Notice>Payout destination saved.</Notice>}
    <Button type="submit" disabled={action.busy || (method !== "Bank" && !(current.registeredPhone ?? (current.method !== "Bank" ? current.account : ""))) || (method === "Bank" && (!bankName.trim() || !accountNumber.trim()))}>{action.busy ? "Saving…" : "Save Destination"}</Button>
  </fieldset></form>}</Resource>;
}
