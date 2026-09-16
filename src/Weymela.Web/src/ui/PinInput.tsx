import { useId, useRef, type ChangeEvent, type ClipboardEvent, type KeyboardEvent } from "react";

const PIN_LENGTH = 5;

type PinInputProps = {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | null;
  autoFocus?: boolean;
};

export function PinInput({ label, value, onChange, error, autoFocus = false }: PinInputProps) {
  const id = useId();
  const inputs = useRef<(HTMLInputElement | null)[]>([]);
  const digits = value.padEnd(PIN_LENGTH, " ").slice(0, PIN_LENGTH).split("");

  const focus = (index: number) => inputs.current[index]?.focus();
  const update = (index: number, digit: string) => {
    const next = [...digits];
    next[index] = digit || " ";
    onChange(next.join(""));
  };

  const handleChange = (event: ChangeEvent<HTMLInputElement>, index: number) => {
    const digit = event.target.value;
    if (digit === "") {
      update(index, "");
    } else if (/^[0-9]$/.test(digit)) {
      update(index, digit);
      if (index < PIN_LENGTH - 1) focus(index + 1);
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>, index: number) => {
    if (event.key === "Backspace") {
      event.preventDefault();
      if (digits[index] !== " ") update(index, "");
      else if (index > 0) {
        update(index - 1, "");
        focus(index - 1);
      }
    } else if (event.key === "Delete") {
      event.preventDefault();
      update(index, "");
    } else if (event.key === "ArrowLeft" && index > 0) {
      event.preventDefault();
      focus(index - 1);
    } else if (event.key === "ArrowRight" && index < PIN_LENGTH - 1) {
      event.preventDefault();
      focus(index + 1);
    } else if (event.key === "Home") {
      event.preventDefault();
      focus(0);
    } else if (event.key === "End") {
      event.preventDefault();
      focus(PIN_LENGTH - 1);
    }
  };

  const handlePaste = (event: ClipboardEvent<HTMLInputElement>) => {
    event.preventDefault();
    const pasted = event.clipboardData.getData("text");
    if (/^[0-9]{5}$/.test(pasted)) {
      onChange(pasted);
      focus(PIN_LENGTH - 1);
    }
  };

  return <div className="pin-input" role="group" aria-labelledby={`${id}-label`}>
    <span className="pin-input-label" id={`${id}-label`}>{label}</span>
    <div className="pin-input-cells">
      {digits.map((digit, index) => <input
        key={index}
        ref={(element) => { inputs.current[index] = element; }}
        type="password"
        inputMode="numeric"
        autoComplete="off"
        pattern="[0-9]*"
        maxLength={1}
        aria-label={`${label}, digit ${index + 1} of ${PIN_LENGTH}`}
        aria-invalid={Boolean(error)}
        aria-describedby={error ? `${id}-error` : undefined}
        autoFocus={autoFocus && index === 0}
        value={digit === " " ? "" : digit}
        onFocus={(event) => event.currentTarget.select()}
        onClick={(event) => event.currentTarget.select()}
        onChange={(event) => handleChange(event, index)}
        onKeyDown={(event) => handleKeyDown(event, index)}
        onPaste={handlePaste}
      />)}
    </div>
    {error ? <span className="sr-only" id={`${id}-error`}>{error}</span> : null}
  </div>;
}
