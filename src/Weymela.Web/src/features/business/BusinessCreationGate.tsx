import type { ReactNode } from "react";
import { useResource } from "../../api/client";
import type { Wallet } from "../../api/types";
import { Button, Notice, Resource, Section } from "../../ui/components";

export function BusinessCreationGate({ children }: { children: (wallet: Wallet) => ReactNode }) {
  const wallet = useResource<Wallet>("/business/wallet");
  if (wallet.loading) return <p className="loading" role="status">Checking available funds…</p>;
  if (wallet.error) return <Section title="Creation unavailable"><Notice error>{wallet.error.message}</Notice><Button variant="secondary" onClick={wallet.reload}>Try again</Button></Section>;
  return <Resource resource={wallet}>{children}</Resource>;
}
