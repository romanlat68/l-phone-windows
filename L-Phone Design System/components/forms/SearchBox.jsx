import React from "react";
import { Icon } from "../core/Icon.jsx";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-search {
    display: flex; align-items: center; gap: 8px; box-sizing: border-box;
    background: var(--lp-control-fill-input, #fbfbfb); border: 1px solid var(--stroke-control);
    border-bottom-color: var(--control-bottom-accent, rgba(0,0,0,.45));
    border-radius: var(--radius-sm, 4px); min-height: var(--control-height, 32px); padding: 0 8px 0 10px;
    font-family: var(--font-sans);
  }
  .lp-search:focus-within { background: var(--surface-card, #fff); border-bottom: 2px solid var(--accent-default); }
  .lp-search input {
    flex: 1; min-width: 0; border: 0; outline: 0; background: transparent;
    font-family: var(--font-sans); font-size: 14px; color: var(--text-primary); padding: 5px 0;
  }
  .lp-search input::placeholder { color: var(--text-tertiary); }
  .lp-search__btn { display: inline-flex; border: 0; background: transparent; padding: 4px; cursor: pointer; border-radius: var(--radius-sm,4px); }
  .lp-search__btn:hover { background: var(--lp-subtle-hover); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "searchbox");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * SearchBox — Fluent AutoSuggestBox-style search input with a trailing
 * Find icon. Used in Contacts.
 */
export function SearchBox({
  placeholder = "Поиск",
  value,
  defaultValue,
  onChange,
  onSubmit,
  className = "",
  style,
  ...rest
}) {
  ensureStyles();
  const ref = React.useRef(null);
  return (
    <div className={["lp-search", className].filter(Boolean).join(" ")} style={style}>
      <input
        ref={ref}
        type="search"
        placeholder={placeholder}
        value={value}
        defaultValue={defaultValue}
        onChange={onChange}
        onKeyDown={(e) => {
          if (e.key === "Enter" && onSubmit) onSubmit(e.currentTarget.value);
        }}
        {...rest}
      />
      <button
        type="button"
        className="lp-search__btn"
        aria-label="Найти"
        onClick={() => onSubmit && onSubmit(ref.current ? ref.current.value : "")}
      >
        <Icon name="search" size={15} color="#5d5d5d" />
      </button>
    </div>
  );
}
