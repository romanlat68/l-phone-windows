import React from "react";
import { Icon } from "../core/Icon.jsx";

let _injected = false;
function ensureStyles() {
  if (_injected || typeof document === "undefined") return;
  _injected = true;
  const css = `
  .lp-select { display: inline-flex; flex-direction: column; gap: 4px; font-family: var(--font-sans); position: relative; }
  .lp-select__label { font-size: 14px; line-height: 20px; color: var(--text-primary); }
  .lp-select__trigger {
    display: flex; align-items: center; gap: 8px; width: 100%; box-sizing: border-box;
    min-height: var(--control-height, 32px); padding: 5px 8px 5px 10px;
    background: var(--lp-control-fill, #fff); border: 1px solid var(--stroke-control);
    border-bottom-color: var(--control-bottom-accent, rgba(0,0,0,.45));
    border-radius: var(--radius-sm, 4px); cursor: pointer; text-align: left;
    font-size: 14px; color: var(--text-primary);
  }
  .lp-select__trigger:hover { background: var(--lp-control-fill-hover, #f9f9f9); }
  .lp-select__value { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .lp-select__placeholder { color: var(--text-tertiary); }
  .lp-select__flyout {
    position: absolute; left: 0; right: 0; top: 100%; margin-top: 4px; z-index: 40;
    background: var(--surface-card, #fff); border: 1px solid var(--stroke-card);
    border-radius: var(--radius-md, 8px); box-shadow: var(--shadow-flyout); padding: 4px;
    max-height: 240px; overflow: auto;
  }
  .lp-select__opt {
    display: flex; align-items: center; gap: 8px; padding: 7px 10px; border-radius: var(--radius-sm,4px);
    font-size: 14px; color: var(--text-primary); cursor: pointer;
  }
  .lp-select__opt:hover { background: var(--lp-subtle-hover); }
  .lp-select__opt[data-selected="true"] { background: var(--lp-subtle-hover); font-weight: var(--fw-semibold,600); }
  `;
  const el = document.createElement("style");
  el.setAttribute("data-lp", "select");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * Select — Fluent ComboBox: a trigger that opens a flyout list of options.
 * Options are { value, label }. Controlled via `value` + `onChange`.
 */
export function Select({
  label,
  options = [],
  value,
  defaultValue,
  onChange,
  placeholder = "Выберите…",
  className = "",
  style,
}) {
  ensureStyles();
  const isControlled = value !== undefined;
  const [internal, setInternal] = React.useState(defaultValue);
  const [open, setOpen] = React.useState(false);
  const current = isControlled ? value : internal;
  const ref = React.useRef(null);
  React.useEffect(() => {
    function onDoc(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    }
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, []);
  const selected = options.find((o) => o.value === current);
  const pick = (val) => {
    if (!isControlled) setInternal(val);
    onChange && onChange(val);
    setOpen(false);
  };
  return (
    <span className={["lp-select", className].filter(Boolean).join(" ")} style={style} ref={ref}>
      {label ? <span className="lp-select__label">{label}</span> : null}
      <button type="button" className="lp-select__trigger" onClick={() => setOpen((o) => !o)}>
        <span className={"lp-select__value" + (selected ? "" : " lp-select__placeholder")}>
          {selected ? selected.label : placeholder}
        </span>
        <Icon name="chevron-down" size={12} color="#5d5d5d" />
      </button>
      {open ? (
        <div className="lp-select__flyout">
          {options.map((o) => (
            <div
              key={o.value}
              className="lp-select__opt"
              data-selected={o.value === current ? "true" : "false"}
              onClick={() => pick(o.value)}
            >
              {o.value === current ? <Icon name="check-mark" size={12} color="#22334d" /> : <span style={{ width: 12 }} />}
              <span>{o.label}</span>
            </div>
          ))}
        </div>
      ) : null}
    </span>
  );
}
