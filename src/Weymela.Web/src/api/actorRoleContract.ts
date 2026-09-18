export const actorRoleWireValues = {
  PlatformAdmin: 0,
  OperationsAdmin: 1,
  Business: 2,
  Creator: 3,
  Customer: 4,
  Cashier: 5,
} as const;

export type ActorRoleName = keyof typeof actorRoleWireValues;

export function actorRoleNameFromWire(value: ActorRoleName | number): ActorRoleName | null {
  if (typeof value === "string")
    return Object.hasOwn(actorRoleWireValues, value) ? value : null;
  const match = (Object.entries(actorRoleWireValues) as [ActorRoleName, number][])
    .find(([, wireValue]) => wireValue === value);
  return match?.[0] ?? null;
}
