import React from "react";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-switch { display: inline-flex; flex-direction: column; gap: 6px; font-family: var(--font-sans); }
  .lp-switch__header { font-size: 14px; line-height: 20px; color: var(--text-primary); }
  .lp-switch__row { display: inline-flex; align-items: center; gap: 10px; cursor: pointer; user-select: none; }
  .lp-switch__track {
    position: relative; width: 40px; height: 20px; flex: none; border-radius: 10px;
    background: transparent; border: 1px solid var(--control-bottom-accent, rgba(0,0,0,.45));
    transition: background var(--dur-fast,.1s) var(--ease-standard), border-color var(--dur-fast,.1s);
  }
  .lp-switch__knob {
    position: absolute; top: 50%; left: 3px; transform: translateY(-50%);
    width: 12px; height: 12px; border-radius: 50%; background: var(--control-bottom-accent, rgba(0,0,0,.6));
    transition: left var(--dur-normal,.167s) var(--ease-standard), width var(--dur-fast,.1s), background var(--dur-fast,.1s);
  }
  .lp-switch__row:hover .lp-switch__track { background: var(--lp-subtle-hover); }
  .lp-switch[data-on="true"] .lp-switch__track { background: var(--accent-default); border-color: var(--accent-default); }
  .lp-switch[data-on="true"] .lp-switch__knob { left: 22px; width: 14px; height: 14px; background: #fff; }
  .lp-switch[data-on="true"] .lp-switch__row:hover .lp-switch__track { background: var(--accent-hover); }
  .lp-switch__state { font-size: 14px; color: var(--text-secondary); min-width: 0; }
  .lp-switch[data-disabled="true"] { opacity: .5; }
  .lp-switch[data-disabled="true"] .lp-switch__row { pointer-events: none; }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "switch");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * ToggleSwitch — Fluent on/off switch with optional header and state text.
 * Controlled via `checked` + `onChange`, or uncontrolled via `defaultChecked`.
 */
export function ToggleSwitch({
  label,
  checked,
  defaultChecked = false,
  onChange,
  onText,
  offText,
  disabled = false,
  className = "",
  style,
}) {
  ensureStyles();
  const isControlled = checked !== undefined;
  const [internal, setInternal] = React.useState(defaultChecked);
  const on = isControlled ? checked : internal;
  const toggle = () => {
    if (disabled) return;
    if (!isControlled) setInternal(!on);
    onChange && onChange(!on);
  };
  const stateText = on ? onText : offText;
  return (
    <span
      className={["lp-switch", className].filter(Boolean).join(" ")}
      data-on={on ? "true" : "false"}
      data-disabled={disabled ? "true" : "false"}
      style={style}
    >
      {label ? <span className="lp-switch__header">{label}</span> : null}
      <span
        className="lp-switch__row"
        role="switch"
        aria-checked={on}
        tabIndex={disabled ? -1 : 0}
        onClick={toggle}
        onKeyDown={(e) => {
          if (e.key === " " || e.key === "Enter") {
            e.preventDefault();
            toggle();
          }
        }}
      >
        <span className="lp-switch__track">
          <span className="lp-switch__knob" />
        </span>
        {stateText ? <span className="lp-switch__state">{stateText}</span> : null}
      </span>
    </span>
  );
}
