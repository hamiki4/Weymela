import { useId, useState } from "react";

export function PasswordField({
  label,
  value,
  onChange,
  autoComplete,
  autoFocus = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: "current-password" | "new-password";
  autoFocus?: boolean;
}) {
  const inputId = useId();
  const [shown, setShown] = useState(false);
  return <div className="field password-field">
    <label htmlFor={inputId}>{label}</label>
    <div className="password-control">
      <input
        id={inputId}
        type={shown ? "text" : "password"}
        autoComplete={autoComplete}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        minLength={autoComplete === "new-password" ? 12 : undefined}
        maxLength={128}
        required
        autoFocus={autoFocus}
      />
      <button type="button" className="password-toggle" aria-label={`${shown ? "Hide" : "Show"} ${label.toLowerCase()}`}
        aria-pressed={shown} onClick={() => setShown((current) => !current)}>
        {shown ? "Hide" : "Show"}
      </button>
    </div>
  </div>;
}
