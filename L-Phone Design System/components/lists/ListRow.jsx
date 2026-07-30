import React from "react";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-listrow {
    display: flex; align-items: center; gap: 12px; width: 100%; box-sizing: border-box;
    border: 0; background: transparent; cursor: pointer; text-align: left;
    padding: 8px 10px; border-radius: var(--radius-sm, 4px); font-family: var(--font-sans);
    transition: background var(--dur-fast,.1s);
  }
  .lp-listrow:hover { background: var(--lp-subtle-hover); }
  .lp-listrow:active { background: var(--lp-subtle-pressed); }
  .lp-listrow__body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
  .lp-listrow__title { font-size: 14px; line-height: 20px; color: var(--text-primary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .lp-listrow__sub { font-size: 12px; line-height: 16px; color: var(--text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .lp-listrow__trailing { flex: none; display: flex; align-items: center; gap: 4px; color: var(--text-tertiary); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "listrow");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * ListRow — a single row in a Contacts or History list: optional leading
 * element (Avatar/Icon), title, subtitle, and trailing content.
 */
export function ListRow({ leading, title, subtitle, trailing, className = "", ...rest }) {
  ensureStyles();
  return (
    <button className={["lp-listrow", className].filter(Boolean).join(" ")} {...rest}>
      {leading}
      <span className="lp-listrow__body">
        <span className="lp-listrow__title">{title}</span>
        {subtitle != null ? <span className="lp-listrow__sub">{subtitle}</span> : null}
      </span>
      {trailing != null ? <span className="lp-listrow__trailing">{trailing}</span> : null}
    </button>
  );
}
