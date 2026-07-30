import React from "react";
import { Icon } from "./Icon.jsx";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-iconbtn {
    display: inline-flex; align-items: center; justify-content: center;
    box-sizing: border-box; border: 0; padding: 0; cursor: pointer; flex: none;
    border-radius: var(--radius-round, 50%);
    transition: background var(--dur-fast,.1s) var(--ease-standard), transform var(--dur-fast,.1s);
  }
  .lp-iconbtn:active { transform: scale(0.94); }
  .lp-iconbtn:focus-visible { outline: 2px solid var(--text-on-navy, #fff); outline-offset: 2px; }
  .lp-iconbtn[disabled] { cursor: default; pointer-events: none; opacity: .45; }

  .lp-iconbtn--subtle { background: transparent; }
  .lp-iconbtn--subtle:hover { background: var(--lp-subtle-hover); }
  .lp-iconbtn--subtle:active { background: var(--lp-subtle-pressed); }

  .lp-iconbtn--glass { background: var(--call-control-fill, rgba(255,255,255,.20)); }
  .lp-iconbtn--glass:hover { background: var(--call-control-fill-hover, rgba(255,255,255,.30)); }

  .lp-iconbtn--accept { background: var(--call-accept, #2ba84a); box-shadow: var(--shadow-call-fab); }
  .lp-iconbtn--accept:hover { background: var(--call-accept-hover, #1f8a3c); }

  .lp-iconbtn--end { background: var(--call-end, #c42b1c); }
  .lp-iconbtn--end:hover { background: var(--call-end-hover, #a32218); }

  .lp-iconbtn--accent { background: var(--accent-default); }
  .lp-iconbtn--accent:hover { background: var(--accent-hover); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "iconbutton");
  el.textContent = css;
  document.head.appendChild(el);
}

const TONE = {
  subtle: "#1a1a1a",
  glass: "#ffffff",
  accept: "#ffffff",
  end: "#ffffff",
  accent: "#ffffff",
};

/**
 * IconButton — circular, icon-only control. Variants cover the dialer's
 * call FAB (accept), hang-up (end), in-call glass controls, and subtle
 * header buttons.
 */
export function IconButton({
  variant = "subtle",
  size = 48,
  icon,
  iconColor,
  iconSize,
  title,
  className = "",
  ...rest
}) {
  ensureStyles();
  const cls = ["lp-iconbtn", `lp-iconbtn--${variant}`, className].filter(Boolean).join(" ");
  return (
    <button
      className={cls}
      style={{ width: size, height: size }}
      title={title}
      aria-label={title}
      {...rest}
    >
      <Icon
        name={icon}
        size={iconSize || Math.round(size * 0.42)}
        color={iconColor || TONE[variant]}
      />
    </button>
  );
}
