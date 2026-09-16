import { useId, type ReactNode } from "react";

export type OnboardingRole = "Customer" | "Creator" | "Business";

const roleClass: Record<OnboardingRole, string> = {
  Customer: "role-customer",
  Creator: "role-creator",
  Business: "role-business",
};

export function RoleOnboardingShell({
  role,
  title,
  description,
  children,
}: {
  role: OnboardingRole;
  title: string;
  description: string;
  children: ReactNode;
}) {
  const headingId = useId();
  return (
    <section
      className={`role-onboarding-shell ${roleClass[role]}`}
      aria-labelledby={headingId}
      data-role-theme={role.toLowerCase()}
    >
      <header className="role-onboarding-heading">
        <span className="role-onboarding-label">{role}</span>
        <div>
          <h3 id={headingId}>{title}</h3>
          <p>{description}</p>
        </div>
      </header>
      {children}
    </section>
  );
}
