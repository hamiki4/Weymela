import { useState } from "react";
import { Link } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type { Cashier, CashierCreated } from "../../api/types";
import {
  Button,
  Empty,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { date } from "../../ui/format";

export function BusinessCashiers() {
  const resource = useResource<Cashier[]>("/business/cashiers");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [created, setCreated] = useState<CashierCreated | null>(null);
  const action = useAction();

  const submit = () => void action.run(async (key) => {
    const result = await post<CashierCreated>(
      "/business/cashiers",
      { name, phone },
      key,
    );
    setCreated(result);
    setName("");
    setPhone("");
    resource.reload();
  });

  const changeState = (cashier: Cashier, state: string) =>
    void action.run(async (key) => {
      await post<Cashier>(`/business/cashiers/${cashier.id}/${state}`, undefined, key);
      resource.reload();
    });

  return (
    <>
      <PageHeader
        eyebrow="Business operations"
        title="Cashier Management"
        description="Pre-authorize checkout operators for this Business. Activation codes are temporary and shown only once."
      />
      {created?.activationCode ? (
        <Notice>
          <strong>Cashier created.</strong> Give this temporary activation code
          to {created.cashier.name}: <strong>{created.activationCode}</strong>. Ask them to open <Link to="/cashier/activate">Cashier activation</Link> on their own device.
        </Notice>
      ) : null}
      {action.error ? <Notice error>{action.error}</Notice> : null}
      <Section title="Add Cashier">
        <form
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          <fieldset disabled={action.busy}>
            <div className="two-column">
              <Field label="Cashier name">
                <input
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  autoComplete="name"
                  required
                />
              </Field>
              <Field
                label="Cashier phone number"
                help="Use an Ethiopian mobile number. Weymela will normalize it."
              >
                <input
                  type="tel"
                  inputMode="tel"
                  value={phone}
                  onChange={(event) => setPhone(event.target.value)}
                  autoComplete="tel"
                  required
                />
              </Field>
            </div>
            <Button type="submit">{action.busy ? "Creating…" : "Add Cashier"}</Button>
          </fieldset>
        </form>
      </Section>
      <Section title="Cashiers">
        <Resource resource={resource}>
          {(cashiers) =>
            cashiers.length ? (
              <div className="card-grid">
                {cashiers.map((cashier) => (
                  <article className="workspace-card" key={cashier.id}>
                    <div className="card-head">
                      <div>
                        <h3>{cashier.name}</h3>
                        <p className="muted">{cashier.maskedPhone}</p>
                      </div>
                      <span className="status-badge">{cashier.status}</span>
                    </div>
                    <p className="fine-print">Added {date(cashier.createdAtUtc)}</p>
                    <div className="actions">
                      {cashier.status === "Pending Activation" ? (
                        <>
                          <Button
                            variant="secondary"
                            disabled={action.busy}
                            onClick={() => void action.run(async (key) => {
                              const result = await post<CashierCreated>(
                                `/business/cashiers/${cashier.id}/activation-code`,
                                undefined,
                                key,
                              );
                              setCreated(result);
                            })}
                          >
                            Regenerate code
                          </Button>
                          <Button
                            variant="quiet"
                            disabled={action.busy}
                            onClick={() => changeState(cashier, "disable")}
                          >
                            Cancel
                          </Button>
                        </>
                      ) : cashier.status === "Active" ? (
                        <Button
                          variant="quiet"
                          disabled={action.busy}
                          onClick={() => changeState(cashier, "disable")}
                        >
                          Disable
                        </Button>
                      ) : cashier.status === "Disabled" ? (
                        <Button
                          variant="secondary"
                          disabled={action.busy}
                          onClick={() => changeState(cashier, "enable")}
                        >
                          Enable
                        </Button>
                      ) : null}
                    </div>
                  </article>
                ))}
              </div>
            ) : (
              <Empty
                title="No Cashiers yet"
                message="Add a Cashier when someone else will operate checkout for your Business."
              />
            )
          }
        </Resource>
      </Section>
    </>
  );
}
