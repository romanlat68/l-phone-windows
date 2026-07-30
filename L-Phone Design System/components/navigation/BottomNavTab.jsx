import React from "react";
import { Icon } from "../core/Icon.jsx";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-navbar { display: flex; background: var(--surface-page); border-top: 1px solid var(--stroke-divider); }
  .lp-navtab {
    flex: 1; display: flex; flex-direction: column; align-items: center; gap: 2px;
    border: 0; background: transparent; cursor: pointer; padding: 7px 0 6px;
    font-family: var(--font-sans); color: var(--text-tertiary);
    transition: background var(--dur-fast,.1s), color var(--dur-fast,.1s);
  }
  .lp-navtab:hover { background: var(--lp-subtle-hover); }
  .lp-navtab__label { font-size: var(--type-tab-label, 11px); line-height: 14px; }
  .lp-navtab[data-active="true"] { color: var(--accent-default); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "navtab");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * BottomNavTab — one tab in the app's bottom navigation bar
 * (Набор / Контакты / Журнал). Active tabs take the accent colour.
 * Wrap several in <BottomNavBar>.
 */
export function BottomNavTab({ icon, label, active = false, className = "", ...rest }) {
  ensureStyles();
  return (
    <button
      className={["lp-navtab", className].filter(Boolean).join(" ")}
      data-active={active ? "true" : "false"}
      {...rest}
    >
      <Icon name={icon} size={18} color={active ? "#22334d" : "#8a8a8a"} />
      <span className="lp-navtab__label">{label}</span>
    </button>
  );
}

/** Bottom navigation bar container. */
export function BottomNavBar({ children, className = "", style, ...rest }) {
  ensureStyles();
  return (
    <div className={["lp-navbar", className].filter(Boolean).join(" ")} style={style} {...rest}>
      {children}
    </div>
  );
}
