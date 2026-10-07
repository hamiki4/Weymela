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
    if (resource.data.method === "Bank") setBankName(resource.data.provider);
  }, [resource.data]);
  return <Resource resource={resource}>{current => <form className="payout-destination-form" onSubmit={event => { event.preventDefault(); void action.run(async key => {
    await post(endpoint, method === "Telebirr" ? { method, bankName: null, accountNumber: null } : { method, bankName: bankName.trim(), accountNumber: accountNumber.trim() }, key);
    setAccountNumber(""); setSaved(true); resource.reload();
  }); }}><fieldset disabled={action.busy}>
    <Field label="Payout method"><select value={method} onChange={event => { setMethod(event.target.value); setSaved(false); }}><option value="Telebirr">Telebirr</option><option value="Bank">Bank account</option></select></Field>
    {method === "Telebirr" ? <Field label="Verified Weymela phone"><input value={current.method === "Telebirr" ? current.account : "Your verified phone"} readOnly /></Field> : <>
      <Field label="Bank Name"><input required maxLength={100} value={bankName} onChange={event => { setBankName(event.target.value); setSaved(false); }} /></Field>
      {current.isConfigured && current.method === "Bank" && <p className="fine-print">Current account: {current.account}</p>}
      <Field label="Account Number"><input required inputMode="numeric" pattern="[0-9 ]{6,34}" maxLength={34} value={accountNumber} onChange={event => { setAccountNumber(event.target.value); setSaved(false); }} /></Field>
    </>}
    <p className="fine-print">Recipient: {current.legalName}</p>
    {action.error && <Notice error>{action.error}</Notice>}{saved && <Notice>Payout destination saved.</Notice>}
    <Button type="submit" disabled={action.busy || (method === "Bank" && (!bankName.trim() || !accountNumber.trim()))}>{action.busy ? "Saving…" : "Save Destination"}</Button>
  </fieldset></form>}</Resource>;
}
