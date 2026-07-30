import React from "react";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-dialkey {
    display: flex; flex-direction: column; align-items: center; justify-content: center;
    border: 0; background: transparent; cursor: pointer; color: var(--lp-navy-400, #4e6075);
    height: 54px; border-radius: var(--radius-sm, 4px); font-family: var(--font-sans);
    transition: background var(--dur-fast,.1s) var(--ease-standard);
  }
  .lp-dialkey:hover { background: var(--lp-subtle-hover); }
  .lp-dialkey:active { background: var(--lp-subtle-pressed); }
  .lp-dialkey__digit { font-size: var(--type-dial-digit, 32px); line-height: 1; font-weight: var(--fw-regular, 400); }
  .lp-dialkey__sub { font-size: 13px; line-height: 1; color: var(--text-tertiary); margin-top: 1px; min-height: 13px; }
  .lp-dialpad { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px 24px; }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "dialkey");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * DialKey — a single key on the dial pad: a large numeral with an optional
 * sub-line (letters, or "+" under 0).
 */
export function DialKey({ digit, sub, onPress, className = "", ...rest }) {
  ensureStyles();
  return (
    <button
      className={["lp-dialkey", className].filter(Boolean).join(" ")}
      onClick={() => onPress && onPress(digit)}
      {...rest}
    >
      <span className="lp-dialkey__digit">{digit}</span>
      <span className="lp-dialkey__sub">{sub || "\u00A0"}</span>
    </button>
  );
}

const KEYS = [
  { d: "1", s: "" },
  { d: "2", s: "ABC" },
  { d: "3", s: "DEF" },
  { d: "4", s: "GHI" },
  { d: "5", s: "JKL" },
  { d: "6", s: "MNO" },
  { d: "7", s: "PQRS" },
  { d: "8", s: "TUV" },
  { d: "9", s: "WXYZ" },
  { d: "\u2217", s: "" },
  { d: "0", s: "+" },
  { d: "#", s: "" },
];

/**
 * DialPad — the standard 3×4 keypad. Calls `onPress(char)` for each key.
 */
export function DialPad({ onPress, className = "", style }) {
  ensureStyles();
  return (
    <div className={["lp-dialpad", className].filter(Boolean).join(" ")} style={style}>
      {KEYS.map((k) => (
        <DialKey key={k.d} digit={k.d} sub={k.s} onPress={onPress} />
      ))}
    </div>
  );
}
