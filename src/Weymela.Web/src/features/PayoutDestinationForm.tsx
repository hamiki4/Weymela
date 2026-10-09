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
  const [bankCountry, setBankCountry] = useState("ET");
  const [otherBankCountry, setOtherBankCountry] = useState("");
  const [iban, setIban] = useState("");
  const [swiftBic, setSwiftBic] = useState("");
  const [routingNumber, setRoutingNumber] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    if (!resource.data) return;
    setMethod(resource.data.method);
    setBankName(resource.data.method === "Bank" ? resource.data.provider : "");
    setBankCountry(resource.data.bankCountry && ["ET", "US", "KE", "GB"].includes(resource.data.bankCountry)
      ? resource.data.bankCountry : resource.data.bankCountry ? "OTHER" : "ET");
    setOtherBankCountry(resource.data.bankCountry && !["ET", "US", "KE", "GB"].includes(resource.data.bankCountry)
      ? resource.data.bankCountry : "");
  }, [resource.data]);
  return <Resource resource={resource}>{current => <form className="payout-destination-form" onSubmit={event => { event.preventDefault(); void action.run(async key => {
    await post(endpoint, method !== "Bank" ? { method, bankName: null, accountNumber: null, bankCountry: null, iban: null, swiftBic: null, routingNumber: null }
      : { method, bankName: bankName.trim(), accountNumber: accountNumber.trim(),
        bankCountry: bankCountry === "OTHER" ? otherBankCountry.trim().toUpperCase() : bankCountry,
        iban: iban.trim() || null, swiftBic: swiftBic.trim() || null, routingNumber: routingNumber.trim() || null }, key);
    setAccountNumber(""); setIban(""); setSwiftBic(""); setRoutingNumber(""); setSaved(true); resource.reload();
  }); }}><fieldset disabled={action.busy}>
    <fieldset className="payout-methods"><legend>Payout method</legend>{[["Telebirr", "Telebirr"], ["Mpesa", "M-PESA"], ["Bank", "Bank account"]].map(([value, label]) => <label key={value}><input type="radio" name="payout-method" value={value} checked={method === value} onChange={() => { setMethod(value); setSaved(false); }} />{label}</label>)}</fieldset>
    {method !== "Bank" ? <>
      <Field label="Verified Weymela phone"><input value={current.registeredPhone ?? (current.method !== "Bank" ? current.account : "")} placeholder="No verified phone available" readOnly /></Field>
      {method === "Mpesa" && <p className="fine-print">M-PESA currently supports verified Safaricom Ethiopia +2517 numbers. Wallet ownership is checked before payment.</p>}
      {method === "Telebirr" && <p className="fine-print">Telebirr requires an eligible verified Ethiopian phone number.</p>}
      {method === "Mpesa" && current.registeredPhone && !current.mpesaEligible && <Notice error>This registered phone is not eligible for Weymela&apos;s configured M-PESA service. Choose Bank Account.</Notice>}
      {method === "Telebirr" && current.registeredPhone && !current.telebirrEligible && <Notice error>This registered phone is not eligible for Telebirr. Choose Bank Account.</Notice>}
    </> : <>
      <Field label="Bank country"><select value={bankCountry} onChange={event => { setBankCountry(event.target.value); setSaved(false); }}>
        <option value="ET">Ethiopia</option><option value="US">United States</option><option value="KE">Kenya</option><option value="GB">United Kingdom</option><option value="OTHER">Other country</option>
      </select></Field>
      {bankCountry === "OTHER" && <Field label="Two-letter country code"><input required pattern="[A-Za-z]{2}" maxLength={2} value={otherBankCountry} onChange={event => { setOtherBankCountry(event.target.value.toUpperCase()); setSaved(false); }} /></Field>}
      <Field label="Bank Name"><input required maxLength={100} value={bankName} onChange={event => { setBankName(event.target.value); setSaved(false); }} /></Field>
      {current.isConfigured && current.method === "Bank" && <p className="fine-print">Current account: {current.account}</p>}
      <Field label="Account Number"><input required inputMode="text" pattern="[A-Za-z0-9 -]{4,34}" maxLength={34} value={accountNumber} onChange={event => { setAccountNumber(event.target.value); setSaved(false); }} /></Field>
      {bankCountry === "US" && <Field label="Routing number"><input required inputMode="numeric" pattern="[0-9]{9}" maxLength={9} value={routingNumber} onChange={event => { setRoutingNumber(event.target.value); setSaved(false); }} /></Field>}
      {bankCountry === "GB" && <Field label="IBAN"><input required autoCapitalize="characters" maxLength={34} value={iban} onChange={event => { setIban(event.target.value); setSaved(false); }} /></Field>}
      {bankCountry !== "ET" && bankCountry !== "US" && <Field label="SWIFT / BIC"><input required autoCapitalize="characters" pattern="[A-Za-z0-9]{8}([A-Za-z0-9]{3})?" maxLength={11} value={swiftBic} onChange={event => { setSwiftBic(event.target.value); setSaved(false); }} /></Field>}
      <p className="fine-print">Saving bank details does not guarantee an international transfer. Weymela verifies route availability and recipient details before recording a manual payment.</p>
    </>}
    <p className="fine-print">Recipient: <span data-no-translate>{current.legalName}</span></p>
    {action.error && <Notice error>{action.error}</Notice>}{saved && <Notice>Payout destination saved.</Notice>}
    <Button type="submit" disabled={action.busy
      || method === "Telebirr" && !current.telebirrEligible
      || method === "Mpesa" && !current.mpesaEligible
      || method === "Bank" && (!bankName.trim() || !accountNumber.trim()
        || bankCountry === "OTHER" && !/^[A-Za-z]{2}$/.test(otherBankCountry)
        || bankCountry === "US" && !/^\d{9}$/.test(routingNumber)
        || bankCountry === "GB" && !iban.trim()
        || bankCountry !== "ET" && bankCountry !== "US" && !swiftBic.trim())}>{action.busy ? "Saving…" : "Save Destination"}</Button>
  </fieldset></form>}</Resource>;
}
