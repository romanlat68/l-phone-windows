import React from "react";
import { Icon } from "../core/Icon.jsx";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-rail { display: flex; flex-direction: column; gap: 2px; padding: 4px; font-family: var(--font-sans); }
  .lp-railitem {
    position: relative; display: flex; align-items: center; gap: 12px;
    border: 0; background: transparent; cursor: pointer; width: 100%; text-align: left;
    padding: 9px 12px; border-radius: var(--radius-sm, 4px); color: var(--text-primary); font-size: 14px;
    transition: background var(--dur-fast,.1s);
  }
  .lp-railitem:hover { background: var(--lp-subtle-hover); }
  .lp-railitem[data-selected="true"] { background: var(--lp-subtle-hover); }
  .lp-railitem[data-selected="true"]::before {
    content: ""; position: absolute; left: 0; top: 50%; transform: translateY(-50%);
    width: 3px; height: 16px; border-radius: 2px; background: var(--accent-default);
  }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "railitem");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * NavRailItem — a Fluent NavigationView entry for the Settings left rail.
 * Each item carries a coloured icon and a selected accent bar.
 */
export function NavRailItem({ icon, iconColor = "#0078d7", label, selected = false, className = "", ...rest }) {
  ensureStyles();
  return (
    <button
      className={["lp-railitem", className].filter(Boolean).join(" ")}
      data-selected={selected ? "true" : "false"}
      {...rest}
    >
      <Icon name={icon} size={16} color={iconColor} />
      <span>{label}</span>
    </button>
  );
}

/** Settings rail container. */
export function NavRail({ children, className = "", style, ...rest }) {
  ensureStyles();
  return (
    <nav className={["lp-rail", className].filter(Boolean).join(" ")} style={style} {...rest}>
      {children}
    </nav>
  );
}
