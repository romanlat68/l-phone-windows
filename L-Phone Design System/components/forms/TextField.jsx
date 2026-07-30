import React from "react";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-field { display: flex; flex-direction: column; gap: 4px; font-family: var(--font-sans); }
  .lp-field__label { font-size: 14px; line-height: 20px; color: var(--text-primary); }
  .lp-field__hint { font-size: 12px; line-height: 16px; color: var(--text-secondary); }
  .lp-field__control {
    display: flex; align-items: center; gap: 8px;
    background: var(--lp-control-fill-input, #fbfbfb);
    border: 1px solid var(--stroke-control);
    border-bottom: 1px solid var(--control-bottom-accent, rgba(0,0,0,.45));
    border-radius: var(--radius-sm, 4px);
    min-height: var(--control-height, 32px); padding: 0 10px; box-sizing: border-box;
    transition: border-color var(--dur-fast,.1s), background var(--dur-fast,.1s);
  }
  .lp-field__control:hover { background: var(--lp-control-fill-hover, #f9f9f9); }
  .lp-field__control:focus-within {
    background: var(--surface-card, #fff);
    border-bottom: 2px solid var(--accent-default);
    padding-bottom: 0;
  }
  .lp-field__control input {
    flex: 1; min-width: 0; border: 0; outline: 0; background: transparent;
    font-family: var(--font-sans); font-size: 14px; line-height: 20px;
    color: var(--text-primary); padding: 5px 0;
  }
  .lp-field__control input::placeholder { color: var(--text-tertiary); }
  .lp-field--invalid .lp-field__control { border-color: var(--state-danger); border-bottom-color: var(--state-danger); }
  .lp-field--disabled .lp-field__control { background: var(--lp-subtle-hover); border-color: var(--stroke-card); }
  .lp-field--disabled input { color: var(--text-disabled); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "textfield");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * TextField — Fluent single-line text input with optional header label,
 * hint and adornments. Shows the signature 2px accent underline on focus.
 */
export function TextField({
  label,
  hint,
  placeholder,
  value,
  defaultValue,
  onChange,
  type = "text",
  invalid = false,
  disabled = false,
  prefix,
  suffix,
  className = "",
  style,
  ...rest
}) {
  ensureStyles();
  const cls = [
    "lp-field",
    invalid ? "lp-field--invalid" : "",
    disabled ? "lp-field--disabled" : "",
    className,
  ]
    .filter(Boolean)
    .join(" ");
  return (
    <label className={cls} style={style}>
      {label ? <span className="lp-field__label">{label}</span> : null}
      <span className="lp-field__control">
        {prefix}
        <input
          type={type}
          placeholder={placeholder}
          value={value}
          defaultValue={defaultValue}
          onChange={onChange}
          disabled={disabled}
          {...rest}
        />
        {suffix}
      </span>
      {hint ? <span className="lp-field__hint">{hint}</span> : null}
    </label>
  );
}
