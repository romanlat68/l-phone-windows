import React from "react";
import { Icon } from "./Icon.jsx";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-btn {
    display: inline-flex; align-items: center; justify-content: center; gap: 8px;
    box-sizing: border-box; min-height: var(--control-height, 32px);
    padding: 4px 12px; border-radius: var(--radius-sm, 4px);
    font-family: var(--font-sans); font-size: 14px; font-weight: var(--fw-regular, 400);
    line-height: 20px; cursor: pointer; user-select: none; white-space: nowrap;
    border: 1px solid transparent; transition: background var(--dur-fast,.1s) var(--ease-standard), border-color var(--dur-fast,.1s);
  }
  .lp-btn:focus-visible { outline: 2px solid var(--text-primary); outline-offset: 1px; }
  .lp-btn[disabled] { cursor: default; pointer-events: none; }
  .lp-btn--block { width: 100%; }
  .lp-btn--sm { min-height: var(--control-height-sm, 28px); padding: 2px 10px; font-size: 13px; }

  /* Standard */
  .lp-btn--standard { background: var(--lp-control-fill, #fff); color: var(--text-primary);
    border-color: var(--stroke-control); border-bottom-color: rgba(0,0,0,.16); }
  .lp-btn--standard:hover { background: var(--lp-control-fill-hover, #f9f9f9); }
  .lp-btn--standard:active { background: var(--lp-subtle-pressed); color: var(--text-secondary); border-bottom-color: var(--stroke-control); }
  .lp-btn--standard[disabled] { background: var(--lp-subtle-hover); color: var(--text-disabled); border-color: var(--stroke-card); }

  /* Accent */
  .lp-btn--accent { background: var(--accent-default); color: var(--text-on-accent);
    border-color: transparent; border-bottom-color: rgba(0,0,0,.22); font-weight: var(--fw-regular,400); }
  .lp-btn--accent:hover { background: var(--accent-hover); }
  .lp-btn--accent:active { background: var(--accent-pressed); color: rgba(255,255,255,.78); }
  .lp-btn--accent[disabled] { background: var(--lp-subtle-hover); color: var(--text-disabled); }

  /* Subtle */
  .lp-btn--subtle { background: transparent; color: var(--text-primary); border-color: transparent; }
  .lp-btn--subtle:hover { background: var(--lp-subtle-hover); }
  .lp-btn--subtle:active { background: var(--lp-subtle-pressed); color: var(--text-secondary); }
  .lp-btn--subtle[disabled] { color: var(--text-disabled); }

  /* Destructive (standard chrome, red ink) */
  .lp-btn--destructive { background: var(--lp-control-fill, #fff); color: var(--state-danger);
    border-color: var(--stroke-control); border-bottom-color: rgba(0,0,0,.16); }
  .lp-btn--destructive:hover { background: rgba(196,43,28,.06); }
  .lp-btn--destructive:active { background: rgba(196,43,28,.10); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "button");
  el.textContent = css;
  document.head.appendChild(el);
}

const ICON_TONE = {
  standard: "#1a1a1a",
  accent: "#ffffff",
  subtle: "#1a1a1a",
  destructive: "#c42b1c",
};

/**
 * Button — Fluent push button. Variants: standard | accent | subtle | destructive.
 */
export function Button({
  variant = "standard",
  size = "md",
  block = false,
  icon,
  iconColor,
  children,
  className = "",
  ...rest
}) {
  ensureStyles();
  const cls = [
    "lp-btn",
    `lp-btn--${variant}`,
    size === "sm" ? "lp-btn--sm" : "",
    block ? "lp-btn--block" : "",
    className,
  ]
    .filter(Boolean)
    .join(" ");
  return (
    <button className={cls} {...rest}>
      {icon ? (
        <Icon name={icon} size={size === "sm" ? 14 : 16} color={iconColor || ICON_TONE[variant]} />
      ) : null}
      {children != null ? <span>{children}</span> : null}
    </button>
  );
}
