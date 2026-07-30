/* @ds-bundle: {"format":3,"namespace":"LPhoneDesignSystem_5e292e","components":[{"name":"Avatar","sourcePath":"components/core/Avatar.jsx"},{"name":"Badge","sourcePath":"components/core/Badge.jsx"},{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Card","sourcePath":"components/core/Card.jsx"},{"name":"Icon","sourcePath":"components/core/Icon.jsx"},{"name":"IconButton","sourcePath":"components/core/IconButton.jsx"},{"name":"StatusDot","sourcePath":"components/core/StatusDot.jsx"},{"name":"SearchBox","sourcePath":"components/forms/SearchBox.jsx"},{"name":"Select","sourcePath":"components/forms/Select.jsx"},{"name":"TextField","sourcePath":"components/forms/TextField.jsx"},{"name":"ToggleSwitch","sourcePath":"components/forms/ToggleSwitch.jsx"},{"name":"ListRow","sourcePath":"components/lists/ListRow.jsx"},{"name":"BottomNavTab","sourcePath":"components/navigation/BottomNavTab.jsx"},{"name":"BottomNavBar","sourcePath":"components/navigation/BottomNavTab.jsx"},{"name":"DialKey","sourcePath":"components/navigation/DialKey.jsx"},{"name":"DialPad","sourcePath":"components/navigation/DialKey.jsx"},{"name":"NavRailItem","sourcePath":"components/navigation/NavRailItem.jsx"},{"name":"NavRail","sourcePath":"components/navigation/NavRailItem.jsx"}],"sourceHashes":{"components/core/Avatar.jsx":"e0dc91395720","components/core/Badge.jsx":"40699c549534","components/core/Button.jsx":"644bc6721aa6","components/core/Card.jsx":"c9370a0f2945","components/core/Icon.jsx":"956e0e95ac5e","components/core/IconButton.jsx":"d46a3090c04a","components/core/StatusDot.jsx":"f10ef1b9aac8","components/forms/SearchBox.jsx":"643cfe9c721d","components/forms/Select.jsx":"952f9d299514","components/forms/TextField.jsx":"79a6d7ffb309","components/forms/ToggleSwitch.jsx":"e3b626ff65ae","components/lists/ListRow.jsx":"6a6c623770e4","components/navigation/BottomNavTab.jsx":"3d28f179d8c7","components/navigation/DialKey.jsx":"2a24cbcdb0cd","components/navigation/NavRailItem.jsx":"f45eff6c92dd","ui_kits/l-phone/ContactsScreen.jsx":"5433b9f5f292","ui_kits/l-phone/DialerScreen.jsx":"73564e04ff1c","ui_kits/l-phone/Header.jsx":"1b3902b722a9","ui_kits/l-phone/HistoryScreen.jsx":"295115b47cd8","ui_kits/l-phone/InCallBar.jsx":"4c8af752be15","ui_kits/l-phone/Phone.jsx":"b751fb41b0a6","ui_kits/l-phone/SettingsScreen.jsx":"e35a0ae03191","ui_kits/l-phone/data.js":"ca75371be979"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.LPhoneDesignSystem_5e292e = window.LPhoneDesignSystem_5e292e || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/core/Avatar.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const TONES = {
  navy: {
    bg: "var(--surface-brand, #22334d)",
    fg: "#ffffff"
  },
  slate: {
    bg: "var(--lp-slate-100, #cbd3dc)",
    fg: "var(--surface-brand, #22334d)"
  },
  accent: {
    bg: "var(--accent-default, #0078d7)",
    fg: "#ffffff"
  }
};
const STATUS = {
  online: "var(--state-success, #2ba84a)",
  offline: "var(--state-offline, #8a8a8a)",
  busy: "var(--state-danger, #c42b1c)",
  away: "var(--state-warning, #c98a14)"
};

/**
 * Avatar — circular initials badge with an optional registration/presence dot.
 */
function Avatar({
  initials = "—",
  size = 40,
  tone = "slate",
  status,
  ringColor = "var(--surface-brand, #22334d)",
  style,
  ...rest
}) {
  const t = TONES[tone] || TONES.slate;
  const dot = size * 0.3;
  return /*#__PURE__*/React.createElement("span", _extends({
    style: {
      position: "relative",
      display: "inline-flex",
      width: size,
      height: size,
      flex: "none",
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("span", {
    style: {
      width: "100%",
      height: "100%",
      borderRadius: "var(--radius-round, 50%)",
      background: t.bg,
      color: t.fg,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      fontFamily: "var(--font-sans)",
      fontWeight: "var(--fw-semibold, 600)",
      fontSize: Math.round(size * 0.36),
      letterSpacing: "0.01em",
      userSelect: "none"
    }
  }, initials), status ? /*#__PURE__*/React.createElement("span", {
    style: {
      position: "absolute",
      right: 0,
      bottom: 0,
      width: dot,
      height: dot,
      borderRadius: "var(--radius-round, 50%)",
      background: STATUS[status] || STATUS.offline,
      border: `2px solid ${ringColor}`,
      boxSizing: "border-box"
    }
  }) : null);
}
Object.assign(__ds_scope, { Avatar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Avatar.jsx", error: String((e && e.message) || e) }); }

// components/core/Badge.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const TONES = {
  neutral: {
    bg: "var(--lp-subtle-hover)",
    fg: "var(--text-secondary)"
  },
  accent: {
    bg: "var(--accent-default)",
    fg: "#fff"
  },
  success: {
    bg: "var(--state-success)",
    fg: "#fff"
  },
  danger: {
    bg: "var(--state-danger)",
    fg: "#fff"
  },
  navy: {
    bg: "var(--surface-brand)",
    fg: "#fff"
  }
};

/**
 * Badge — small count/label pill. Use `dot` for a minimal count chip on
 * nav tabs (e.g. missed calls).
 */
function Badge({
  tone = "neutral",
  children,
  style,
  ...rest
}) {
  const t = TONES[tone] || TONES.neutral;
  return /*#__PURE__*/React.createElement("span", _extends({
    style: {
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      minWidth: 18,
      height: 18,
      padding: "0 6px",
      borderRadius: "var(--radius-pill, 999px)",
      background: t.bg,
      color: t.fg,
      fontFamily: "var(--font-sans)",
      fontSize: 11,
      fontWeight: "var(--fw-semibold, 600)",
      lineHeight: 1,
      boxSizing: "border-box",
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Badge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Badge.jsx", error: String((e && e.message) || e) }); }

// components/core/Card.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Card — Fluent surface container with hairline stroke and 8px radius.
 * Use `elevation` for flyout/dialog-level shadow.
 */
function Card({
  elevation = "card",
  padding = 16,
  interactive = false,
  className = "",
  style,
  children,
  ...rest
}) {
  const shadow = elevation === "none" ? "none" : elevation === "flyout" ? "var(--shadow-flyout)" : elevation === "dialog" ? "var(--shadow-dialog)" : "var(--shadow-card)";
  return /*#__PURE__*/React.createElement("div", _extends({
    className: className,
    style: {
      background: "var(--surface-card)",
      border: "1px solid var(--stroke-card)",
      borderRadius: "var(--radius-md, 8px)",
      boxShadow: shadow,
      padding,
      boxSizing: "border-box",
      cursor: interactive ? "pointer" : undefined,
      transition: interactive ? "background var(--dur-fast,.1s)" : undefined,
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Card.jsx", error: String((e && e.message) || e) }); }

// components/core/Icon.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Icon — renders a Microsoft Fluent (MDL2) glyph.
 *
 * Cross-platform path: an <img> pointing at the Iconify `fluent-mdl2`
 * SVG endpoint (Microsoft's own MDL2 icon set), with the colour baked
 * into the request. On Windows production builds, prefer the native
 * "Segoe Fluent Icons" font with the matching codepoint instead.
 *
 * Colour can't be inherited through <img>, so pass an explicit `color`
 * (any CSS hex). Defaults to brand navy.
 */
function Icon({
  name,
  size = 16,
  color = "#22334d",
  title,
  style,
  className,
  ...rest
}) {
  const encoded = encodeURIComponent(color);
  const src = `https://api.iconify.design/fluent-mdl2/${name}.svg` + `?color=${encoded}&width=${size}&height=${size}`;
  return /*#__PURE__*/React.createElement("img", _extends({
    src: src,
    width: size,
    height: size,
    alt: title || name,
    "aria-hidden": title ? undefined : true,
    className: className,
    style: {
      display: "inline-block",
      flex: "none",
      ...style
    }
  }, rest));
}
Object.assign(__ds_scope, { Icon });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Icon.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
  destructive: "#c42b1c"
};

/**
 * Button — Fluent push button. Variants: standard | accent | subtle | destructive.
 */
function Button({
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
  const cls = ["lp-btn", `lp-btn--${variant}`, size === "sm" ? "lp-btn--sm" : "", block ? "lp-btn--block" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls
  }, rest), icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: size === "sm" ? 14 : 16,
    color: iconColor || ICON_TONE[variant]
  }) : null, children != null ? /*#__PURE__*/React.createElement("span", null, children) : null);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/IconButton.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
  accent: "#ffffff"
};

/**
 * IconButton — circular, icon-only control. Variants cover the dialer's
 * call FAB (accept), hang-up (end), in-call glass controls, and subtle
 * header buttons.
 */
function IconButton({
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
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls,
    style: {
      width: size,
      height: size
    },
    title: title,
    "aria-label": title
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: iconSize || Math.round(size * 0.42),
    color: iconColor || TONE[variant]
  }));
}
Object.assign(__ds_scope, { IconButton });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/IconButton.jsx", error: String((e && e.message) || e) }); }

// components/core/StatusDot.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const COLORS = {
  online: "var(--state-success, #2ba84a)",
  offline: "var(--state-offline, #8a8a8a)",
  busy: "var(--state-danger, #c42b1c)",
  away: "var(--state-warning, #c98a14)",
  connecting: "var(--state-warning, #c98a14)"
};

/**
 * StatusDot — small filled circle for registration / call state, with an
 * optional inline label (e.g. "Не подключён").
 */
function StatusDot({
  status = "offline",
  size = 10,
  label,
  labelColor = "var(--text-secondary)",
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("span", _extends({
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: 8,
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("span", {
    style: {
      width: size,
      height: size,
      flex: "none",
      borderRadius: "var(--radius-round, 50%)",
      background: COLORS[status] || COLORS.offline
    }
  }), label != null ? /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-sans)",
      fontSize: 13,
      color: labelColor
    }
  }, label) : null);
}
Object.assign(__ds_scope, { StatusDot });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/StatusDot.jsx", error: String((e && e.message) || e) }); }

// components/forms/SearchBox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
function SearchBox({
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
  return /*#__PURE__*/React.createElement("div", {
    className: ["lp-search", className].filter(Boolean).join(" "),
    style: style
  }, /*#__PURE__*/React.createElement("input", _extends({
    ref: ref,
    type: "search",
    placeholder: placeholder,
    value: value,
    defaultValue: defaultValue,
    onChange: onChange,
    onKeyDown: e => {
      if (e.key === "Enter" && onSubmit) onSubmit(e.currentTarget.value);
    }
  }, rest)), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "lp-search__btn",
    "aria-label": "\u041D\u0430\u0439\u0442\u0438",
    onClick: () => onSubmit && onSubmit(ref.current ? ref.current.value : "")
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "search",
    size: 15,
    color: "#5d5d5d"
  })));
}
Object.assign(__ds_scope, { SearchBox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/SearchBox.jsx", error: String((e && e.message) || e) }); }

// components/forms/Select.jsx
try { (() => {
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
function Select({
  label,
  options = [],
  value,
  defaultValue,
  onChange,
  placeholder = "Выберите…",
  className = "",
  style
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
  const selected = options.find(o => o.value === current);
  const pick = val => {
    if (!isControlled) setInternal(val);
    onChange && onChange(val);
    setOpen(false);
  };
  return /*#__PURE__*/React.createElement("span", {
    className: ["lp-select", className].filter(Boolean).join(" "),
    style: style,
    ref: ref
  }, label ? /*#__PURE__*/React.createElement("span", {
    className: "lp-select__label"
  }, label) : null, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "lp-select__trigger",
    onClick: () => setOpen(o => !o)
  }, /*#__PURE__*/React.createElement("span", {
    className: "lp-select__value" + (selected ? "" : " lp-select__placeholder")
  }, selected ? selected.label : placeholder), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-down",
    size: 12,
    color: "#5d5d5d"
  })), open ? /*#__PURE__*/React.createElement("div", {
    className: "lp-select__flyout"
  }, options.map(o => /*#__PURE__*/React.createElement("div", {
    key: o.value,
    className: "lp-select__opt",
    "data-selected": o.value === current ? "true" : "false",
    onClick: () => pick(o.value)
  }, o.value === current ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "check-mark",
    size: 12,
    color: "#22334d"
  }) : /*#__PURE__*/React.createElement("span", {
    style: {
      width: 12
    }
  }), /*#__PURE__*/React.createElement("span", null, o.label)))) : null);
}
Object.assign(__ds_scope, { Select });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Select.jsx", error: String((e && e.message) || e) }); }

// components/forms/TextField.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
function TextField({
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
  const cls = ["lp-field", invalid ? "lp-field--invalid" : "", disabled ? "lp-field--disabled" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("label", {
    className: cls,
    style: style
  }, label ? /*#__PURE__*/React.createElement("span", {
    className: "lp-field__label"
  }, label) : null, /*#__PURE__*/React.createElement("span", {
    className: "lp-field__control"
  }, prefix, /*#__PURE__*/React.createElement("input", _extends({
    type: type,
    placeholder: placeholder,
    value: value,
    defaultValue: defaultValue,
    onChange: onChange,
    disabled: disabled
  }, rest)), suffix), hint ? /*#__PURE__*/React.createElement("span", {
    className: "lp-field__hint"
  }, hint) : null);
}
Object.assign(__ds_scope, { TextField });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/TextField.jsx", error: String((e && e.message) || e) }); }

// components/forms/ToggleSwitch.jsx
try { (() => {
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
function ToggleSwitch({
  label,
  checked,
  defaultChecked = false,
  onChange,
  onText,
  offText,
  disabled = false,
  className = "",
  style
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
  return /*#__PURE__*/React.createElement("span", {
    className: ["lp-switch", className].filter(Boolean).join(" "),
    "data-on": on ? "true" : "false",
    "data-disabled": disabled ? "true" : "false",
    style: style
  }, label ? /*#__PURE__*/React.createElement("span", {
    className: "lp-switch__header"
  }, label) : null, /*#__PURE__*/React.createElement("span", {
    className: "lp-switch__row",
    role: "switch",
    "aria-checked": on,
    tabIndex: disabled ? -1 : 0,
    onClick: toggle,
    onKeyDown: e => {
      if (e.key === " " || e.key === "Enter") {
        e.preventDefault();
        toggle();
      }
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "lp-switch__track"
  }, /*#__PURE__*/React.createElement("span", {
    className: "lp-switch__knob"
  })), stateText ? /*#__PURE__*/React.createElement("span", {
    className: "lp-switch__state"
  }, stateText) : null));
}
Object.assign(__ds_scope, { ToggleSwitch });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/ToggleSwitch.jsx", error: String((e && e.message) || e) }); }

// components/lists/ListRow.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
function ListRow({
  leading,
  title,
  subtitle,
  trailing,
  className = "",
  ...rest
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("button", _extends({
    className: ["lp-listrow", className].filter(Boolean).join(" ")
  }, rest), leading, /*#__PURE__*/React.createElement("span", {
    className: "lp-listrow__body"
  }, /*#__PURE__*/React.createElement("span", {
    className: "lp-listrow__title"
  }, title), subtitle != null ? /*#__PURE__*/React.createElement("span", {
    className: "lp-listrow__sub"
  }, subtitle) : null), trailing != null ? /*#__PURE__*/React.createElement("span", {
    className: "lp-listrow__trailing"
  }, trailing) : null);
}
Object.assign(__ds_scope, { ListRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/lists/ListRow.jsx", error: String((e && e.message) || e) }); }

// components/navigation/BottomNavTab.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
function BottomNavTab({
  icon,
  label,
  active = false,
  className = "",
  ...rest
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("button", _extends({
    className: ["lp-navtab", className].filter(Boolean).join(" "),
    "data-active": active ? "true" : "false"
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 18,
    color: active ? "#22334d" : "#8a8a8a"
  }), /*#__PURE__*/React.createElement("span", {
    className: "lp-navtab__label"
  }, label));
}

/** Bottom navigation bar container. */
function BottomNavBar({
  children,
  className = "",
  style,
  ...rest
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["lp-navbar", className].filter(Boolean).join(" "),
    style: style
  }, rest), children);
}
Object.assign(__ds_scope, { BottomNavTab, BottomNavBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/BottomNavTab.jsx", error: String((e && e.message) || e) }); }

// components/navigation/DialKey.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
function DialKey({
  digit,
  sub,
  onPress,
  className = "",
  ...rest
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("button", _extends({
    className: ["lp-dialkey", className].filter(Boolean).join(" "),
    onClick: () => onPress && onPress(digit)
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "lp-dialkey__digit"
  }, digit), /*#__PURE__*/React.createElement("span", {
    className: "lp-dialkey__sub"
  }, sub || "\u00A0"));
}
const KEYS = [{
  d: "1",
  s: ""
}, {
  d: "2",
  s: "ABC"
}, {
  d: "3",
  s: "DEF"
}, {
  d: "4",
  s: "GHI"
}, {
  d: "5",
  s: "JKL"
}, {
  d: "6",
  s: "MNO"
}, {
  d: "7",
  s: "PQRS"
}, {
  d: "8",
  s: "TUV"
}, {
  d: "9",
  s: "WXYZ"
}, {
  d: "\u2217",
  s: ""
}, {
  d: "0",
  s: "+"
}, {
  d: "#",
  s: ""
}];

/**
 * DialPad — the standard 3×4 keypad. Calls `onPress(char)` for each key.
 */
function DialPad({
  onPress,
  className = "",
  style
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("div", {
    className: ["lp-dialpad", className].filter(Boolean).join(" "),
    style: style
  }, KEYS.map(k => /*#__PURE__*/React.createElement(DialKey, {
    key: k.d,
    digit: k.d,
    sub: k.s,
    onPress: onPress
  })));
}
Object.assign(__ds_scope, { DialKey, DialPad });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/DialKey.jsx", error: String((e && e.message) || e) }); }

// components/navigation/NavRailItem.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
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
function NavRailItem({
  icon,
  iconColor = "#0078d7",
  label,
  selected = false,
  className = "",
  ...rest
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("button", _extends({
    className: ["lp-railitem", className].filter(Boolean).join(" "),
    "data-selected": selected ? "true" : "false"
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 16,
    color: iconColor
  }), /*#__PURE__*/React.createElement("span", null, label));
}

/** Settings rail container. */
function NavRail({
  children,
  className = "",
  style,
  ...rest
}) {
  ensureStyles();
  return /*#__PURE__*/React.createElement("nav", _extends({
    className: ["lp-rail", className].filter(Boolean).join(" "),
    style: style
  }, rest), children);
}
Object.assign(__ds_scope, { NavRailItem, NavRail });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/NavRailItem.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/ContactsScreen.jsx
try { (() => {
// L-Phone — Contacts (external directory) screen.
function ContactsScreen({
  contacts,
  onPick
}) {
  const {
    SearchBox,
    ListRow,
    Avatar
  } = window.LPhoneDesignSystem_5e292e;
  const [q, setQ] = React.useState("");
  const filtered = contacts.filter(c => (c.name + " " + c.sip).toLowerCase().includes(q.toLowerCase()));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      display: "flex",
      flexDirection: "column"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      padding: "8px 10px 4px"
    }
  }, /*#__PURE__*/React.createElement(SearchBox, {
    placeholder: "\u041F\u043E\u0438\u0441\u043A \u043F\u043E \u0438\u043C\u0435\u043D\u0438 \u0438\u043B\u0438 \u043D\u043E\u043C\u0435\u0440\u0443",
    value: q,
    onChange: e => setQ(e.target.value)
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      overflow: "auto",
      padding: "2px 6px"
    }
  }, filtered.map(c => /*#__PURE__*/React.createElement(ListRow, {
    key: c.sip,
    leading: /*#__PURE__*/React.createElement(Avatar, {
      initials: c.initials,
      size: 38,
      tone: "navy"
    }),
    title: c.name,
    subtitle: c.sip,
    onClick: () => onPick(c.number)
  })), filtered.length === 0 ? /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: "center",
      color: "var(--text-tertiary)",
      fontSize: 13,
      padding: "32px 0"
    }
  }, "\u041D\u0438\u0447\u0435\u0433\u043E \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u043E") : null), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: "6px 12px 10px",
      color: "var(--text-tertiary)",
      fontSize: 12
    }
  }, "\u0412\u043D\u0435\u0448\u043D\u044F\u044F \u0430\u0434\u0440\u0435\u0441\u043D\u0430\u044F \u043A\u043D\u0438\u0433\u0430 \xB7 ", contacts.length, " \u043A\u043E\u043D\u0442\u0430\u043A\u0442\u043E\u0432"));
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  ContactsScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/ContactsScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/DialerScreen.jsx
try { (() => {
// L-Phone — dialer content area: number entry, dial pad, call pill, incoming.
function DialerScreen(props) {
  const {
    number,
    onKey,
    onBackspace,
    onCall,
    callState,
    remote,
    callStatus,
    duration,
    onAccept,
    onDecline
  } = props;
  const {
    DialPad,
    IconButton,
    Button,
    Card
  } = window.LPhoneDesignSystem_5e292e;
  const inCall = callState === "calling" || callState === "active";
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      overflow: "auto",
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      padding: "14px 16px 20px"
    }
  }, callState === "incoming" ? /*#__PURE__*/React.createElement(Card, {
    padding: 16,
    style: {
      width: "100%",
      marginBottom: 8,
      background: "rgba(34,51,77,.07)",
      borderColor: "rgba(34,51,77,.20)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: "center",
      fontWeight: "var(--fw-semibold)",
      marginBottom: 4
    }
  }, "\u0412\u0445\u043E\u0434\u044F\u0449\u0438\u0439 \u0437\u0432\u043E\u043D\u043E\u043A"), /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: "center",
      color: "var(--text-secondary)",
      fontSize: 14,
      marginBottom: 14
    }
  }, remote), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "1fr 1fr",
      gap: 10
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "accent",
    block: true,
    onClick: onAccept
  }, "\u041F\u0440\u0438\u043D\u044F\u0442\u044C"), /*#__PURE__*/React.createElement(Button, {
    variant: "standard",
    block: true,
    onClick: onDecline
  }, "\u041E\u0442\u043A\u043B\u043E\u043D\u0438\u0442\u044C"))) : null, inCall ? /*#__PURE__*/React.createElement("div", {
    style: {
      margin: "14px 0 18px",
      padding: "10px 22px",
      borderRadius: "var(--radius-pill)",
      background: "var(--call-accept)",
      textAlign: "center",
      boxShadow: "0 4px 14px rgba(43,168,74,.30)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      color: "#fff",
      fontSize: 18,
      fontWeight: "var(--fw-semibold)"
    }
  }, remote), /*#__PURE__*/React.createElement("div", {
    style: {
      color: "#e6f4ea",
      fontSize: 13
    }
  }, callState === "active" ? duration : callStatus)) : null, callState === "idle" ? /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      width: "100%",
      maxWidth: 250,
      margin: "36px 0 20px",
      padding: "8px 8px 8px 14px",
      boxSizing: "border-box",
      background: "var(--lp-subtle-hover)",
      border: "1px solid var(--stroke-control)",
      borderRadius: "var(--radius-md)",
      minHeight: 44
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      fontSize: 24,
      color: number ? "var(--text-primary)" : "var(--text-tertiary)",
      fontFamily: "var(--font-sans)",
      overflow: "hidden",
      textOverflow: "ellipsis",
      whiteSpace: "nowrap"
    }
  }, number || "Номер"), number ? /*#__PURE__*/React.createElement("button", {
    onClick: onBackspace,
    title: "\u0421\u0442\u0435\u0440\u0435\u0442\u044C",
    style: {
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      width: 32,
      height: 32,
      flex: "none",
      border: 0,
      borderRadius: "var(--radius-round)",
      background: "transparent",
      cursor: "pointer",
      color: "var(--text-secondary)",
      fontSize: 17,
      lineHeight: 1
    }
  }, "\u232B") : null) : null, callState !== "incoming" ? /*#__PURE__*/React.createElement("div", {
    style: {
      width: "100%",
      maxWidth: 230,
      marginTop: inCall ? 4 : 0
    }
  }, /*#__PURE__*/React.createElement(DialPad, {
    onPress: onKey
  })) : null, callState === "idle" ? /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 18
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    variant: "accept",
    size: 64,
    icon: "phone",
    iconColor: "#ffffff",
    title: "\u041F\u043E\u0437\u0432\u043E\u043D\u0438\u0442\u044C",
    onClick: onCall
  })) : null);
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  DialerScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/DialerScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/Header.jsx
try { (() => {
// L-Phone — account header (navy bar). Shares via window.LPhoneKit.
function Header({
  account,
  registered,
  onOpenSettings,
  onSimulateIncoming
}) {
  const {
    Avatar,
    IconButton
  } = window.LPhoneDesignSystem_5e292e;
  const [menu, setMenu] = React.useState(false);
  React.useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(false);
    const t = setTimeout(() => document.addEventListener("click", close), 0);
    return () => {
      clearTimeout(t);
      document.removeEventListener("click", close);
    };
  }, [menu]);
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: "relative",
      display: "flex",
      alignItems: "center",
      gap: 12,
      background: "var(--surface-brand)",
      padding: "10px 14px"
    }
  }, /*#__PURE__*/React.createElement(Avatar, {
    initials: registered ? account.initials : "—",
    size: 40,
    tone: "slate",
    status: registered ? "online" : "offline",
    ringColor: "#22334d"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      color: "#fff",
      fontSize: 16,
      fontWeight: "var(--fw-semibold)",
      lineHeight: "20px",
      overflow: "hidden",
      textOverflow: "ellipsis",
      whiteSpace: "nowrap"
    }
  }, registered ? account.displayName : "Не подключён"), /*#__PURE__*/React.createElement("div", {
    style: {
      color: "var(--text-on-navy-muted)",
      fontSize: 12,
      lineHeight: "16px",
      overflow: "hidden",
      textOverflow: "ellipsis",
      whiteSpace: "nowrap"
    }
  }, registered ? account.sip : "Откройте настройки для входа")), /*#__PURE__*/React.createElement(IconButton, {
    variant: "glass",
    size: 34,
    icon: "settings",
    iconColor: "#ffffff",
    title: "\u041C\u0435\u043D\u044E",
    onClick: e => {
      e.stopPropagation();
      setMenu(m => !m);
    }
  }), menu ? /*#__PURE__*/React.createElement("div", {
    style: {
      position: "absolute",
      right: 12,
      top: 56,
      zIndex: 50,
      minWidth: 220,
      background: "var(--surface-card)",
      border: "1px solid var(--stroke-card)",
      borderRadius: "var(--radius-md)",
      boxShadow: "var(--shadow-flyout)",
      padding: 4
    }
  }, /*#__PURE__*/React.createElement(MenuItem, {
    icon: "contact",
    label: "\u041C\u043E\u0439 \u0430\u043A\u043A\u0430\u0443\u043D\u0442",
    onClick: onOpenSettings
  }), /*#__PURE__*/React.createElement(MenuItem, {
    icon: "settings",
    label: "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438",
    onClick: onOpenSettings
  }), /*#__PURE__*/React.createElement(MenuItem, {
    icon: "incoming-call",
    label: "\u0421\u0438\u043C\u0443\u043B\u0438\u0440\u043E\u0432\u0430\u0442\u044C \u0432\u0445\u043E\u0434\u044F\u0449\u0438\u0439",
    onClick: onSimulateIncoming
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--stroke-divider)",
      margin: "4px 6px"
    }
  }), /*#__PURE__*/React.createElement(MenuItem, {
    icon: "info",
    label: "\u041E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0435",
    onClick: onOpenSettings
  })) : null);
}
function MenuItem({
  icon,
  label,
  onClick
}) {
  const {
    Icon
  } = window.LPhoneDesignSystem_5e292e;
  const [hover, setHover] = React.useState(false);
  return /*#__PURE__*/React.createElement("button", {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    onClick: onClick,
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12,
      width: "100%",
      textAlign: "left",
      border: 0,
      background: hover ? "var(--lp-subtle-hover)" : "transparent",
      padding: "8px 10px",
      borderRadius: "var(--radius-sm)",
      cursor: "pointer",
      fontFamily: "var(--font-sans)",
      fontSize: 14,
      color: "var(--text-primary)"
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: 16,
    color: "#4e6075"
  }), /*#__PURE__*/React.createElement("span", null, label));
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  Header
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/Header.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/HistoryScreen.jsx
try { (() => {
// L-Phone — Call history (Журнал) screen.
function HistoryScreen({
  history,
  onPick
}) {
  const {
    ListRow,
    Icon,
    Button
  } = window.LPhoneDesignSystem_5e292e;
  const meta = {
    in: {
      icon: "incoming-call",
      color: "#2ba84a",
      label: "Входящий",
      rot: false
    },
    out: {
      icon: "incoming-call",
      color: "#3a4f6f",
      label: "Исходящий",
      rot: true
    },
    missed: {
      icon: "incoming-call",
      color: "#c42b1c",
      label: "Пропущенный",
      rot: false
    }
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      display: "flex",
      flexDirection: "column"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      overflow: "auto",
      padding: "6px 6px"
    }
  }, history.map((h, i) => {
    const m = meta[h.dir];
    return /*#__PURE__*/React.createElement(ListRow, {
      key: i,
      leading: /*#__PURE__*/React.createElement(Icon, {
        name: m.icon,
        size: 20,
        color: m.color,
        style: m.rot ? {
          transform: "rotate(180deg)"
        } : undefined
      }),
      title: /*#__PURE__*/React.createElement("span", {
        style: {
          color: h.missed ? "var(--state-danger)" : "var(--text-primary)"
        }
      }, h.name),
      subtitle: m.label + " · " + h.when,
      trailing: /*#__PURE__*/React.createElement("span", {
        style: {
          fontSize: 12
        }
      }, h.dur),
      onClick: () => onPick(h.number)
    });
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      justifyContent: "center",
      padding: "8px 0 12px",
      borderTop: "1px solid var(--stroke-divider)"
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "subtle",
    size: "sm"
  }, "\u041E\u0447\u0438\u0441\u0442\u0438\u0442\u044C \u0436\u0443\u0440\u043D\u0430\u043B")));
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  HistoryScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/HistoryScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/InCallBar.jsx
try { (() => {
// L-Phone — in-call control bar (navy strip above the bottom nav).
function InCallBar({
  muted,
  held,
  video,
  enabled,
  onMute,
  onHold,
  onVideo,
  onHangup
}) {
  const {
    IconButton
  } = window.LPhoneDesignSystem_5e292e;
  const ctrl = (icon, label, active, onClick, key) => /*#__PURE__*/React.createElement("div", {
    key: key,
    style: {
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      gap: 3
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    variant: active ? "accent" : "glass",
    size: 48,
    icon: icon,
    iconColor: "#ffffff",
    disabled: !enabled,
    onClick: onClick,
    title: label
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 10,
      color: "var(--text-on-navy-muted)"
    }
  }, label));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      justifyContent: "center",
      gap: 14,
      alignItems: "flex-start",
      background: "var(--surface-brand)",
      padding: "10px 8px",
      borderTop: "1px solid var(--stroke-on-navy)"
    }
  }, ctrl("video", "Видео", video, onVideo, "v"), ctrl("microphone", muted ? "Вкл. микр." : "Микрофон", muted, onMute, "m"), ctrl("pause", "Удержание", held, onHold, "h"), ctrl("switch", "Перевод", false, null, "t"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      gap: 3
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    variant: "end",
    size: 48,
    icon: "decline-call",
    iconColor: "#ffffff",
    title: "\u0421\u0431\u0440\u043E\u0441",
    onClick: onHangup
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 10,
      color: "var(--text-on-navy-muted)"
    }
  }, "\u0421\u0431\u0440\u043E\u0441")));
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  InCallBar
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/InCallBar.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/Phone.jsx
try { (() => {
// L-Phone — app shell + call state machine. Composes the kit screens.
function Phone() {
  const NS = window.LPhoneDesignSystem_5e292e;
  const {
    BottomNavBar,
    BottomNavTab
  } = NS;
  const {
    Header,
    DialerScreen,
    InCallBar,
    ContactsScreen,
    HistoryScreen,
    SettingsScreen,
    WinTitleBar
  } = window.LPhoneKit;
  const data = window.LPhoneData;
  const [tab, setTab] = React.useState("dialer");
  const [number, setNumber] = React.useState("");
  const [callState, setCallState] = React.useState("idle"); // idle | incoming | calling | active
  const [remote, setRemote] = React.useState("");
  const [seconds, setSeconds] = React.useState(0);
  const [muted, setMuted] = React.useState(false);
  const [held, setHeld] = React.useState(false);
  const [video, setVideo] = React.useState(false);
  const [settings, setSettings] = React.useState(false);

  // calling -> active after a short ring
  React.useEffect(() => {
    if (callState !== "calling") return;
    const t = setTimeout(() => {
      setCallState("active");
      setSeconds(0);
    }, 1500);
    return () => clearTimeout(t);
  }, [callState]);

  // duration ticker while active and not held
  React.useEffect(() => {
    if (callState !== "active" || held) return;
    const t = setInterval(() => setSeconds(s => s + 1), 1000);
    return () => clearInterval(t);
  }, [callState, held]);
  const fmt = s => `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
  const pretty = n => {
    const c = data.contacts.find(x => x.number === n);
    return c ? c.name : n || "Неизвестный";
  };
  const startCall = () => {
    if (!number) return;
    setRemote(pretty(number));
    setCallState("calling");
    setMuted(false);
    setHeld(false);
    setVideo(false);
  };
  const hangup = () => {
    setCallState("idle");
    setSeconds(0);
    setMuted(false);
    setHeld(false);
    setVideo(false);
  };
  const simulateIncoming = () => {
    setRemote("Анна Кузнецова");
    setCallState("incoming");
    setTab("dialer");
  };
  const accept = () => {
    setCallState("active");
    setSeconds(0);
  };
  const pick = n => {
    setNumber(n);
    setTab("dialer");
  };
  const inCall = callState === "calling" || callState === "active";
  const statusText = callState === "calling" ? "Вызов…" : held ? "На удержании" : "";
  return /*#__PURE__*/React.createElement("div", {
    style: {
      width: 400,
      height: 700,
      display: "flex",
      flexDirection: "column",
      background: "var(--surface-card)",
      borderRadius: 8,
      overflow: "hidden",
      boxShadow: "var(--shadow-dialog)",
      position: "relative"
    }
  }, /*#__PURE__*/React.createElement(WinTitleBar, {
    title: "L-Phone",
    onClose: () => {}
  }), /*#__PURE__*/React.createElement(Header, {
    account: data.account,
    registered: true,
    onOpenSettings: () => setSettings(true),
    onSimulateIncoming: simulateIncoming
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      display: "flex",
      flexDirection: "column",
      background: "var(--surface-card)"
    }
  }, tab === "dialer" ? /*#__PURE__*/React.createElement(DialerScreen, {
    number: number,
    onKey: c => setNumber(n => n + c),
    onBackspace: () => setNumber(n => n.slice(0, -1)),
    onCall: startCall,
    callState: callState,
    remote: remote,
    callStatus: statusText,
    duration: fmt(seconds),
    onAccept: accept,
    onDecline: hangup
  }) : null, tab === "contacts" ? /*#__PURE__*/React.createElement(ContactsScreen, {
    contacts: data.contacts,
    onPick: pick
  }) : null, tab === "history" ? /*#__PURE__*/React.createElement(HistoryScreen, {
    history: data.history,
    onPick: pick
  }) : null), inCall ? /*#__PURE__*/React.createElement(InCallBar, {
    muted: muted,
    held: held,
    video: video,
    enabled: callState === "active",
    onMute: () => setMuted(m => !m),
    onHold: () => setHeld(h => !h),
    onVideo: () => setVideo(v => !v),
    onHangup: hangup
  }) : null, /*#__PURE__*/React.createElement(BottomNavBar, null, /*#__PURE__*/React.createElement(BottomNavTab, {
    icon: "phone",
    label: "\u041D\u0430\u0431\u043E\u0440",
    active: tab === "dialer",
    onClick: () => setTab("dialer")
  }), /*#__PURE__*/React.createElement(BottomNavTab, {
    icon: "contact",
    label: "\u041A\u043E\u043D\u0442\u0430\u043A\u0442\u044B",
    active: tab === "contacts",
    onClick: () => setTab("contacts")
  }), /*#__PURE__*/React.createElement(BottomNavTab, {
    icon: "history",
    label: "\u0416\u0443\u0440\u043D\u0430\u043B",
    active: tab === "history",
    onClick: () => setTab("history")
  })), settings ? /*#__PURE__*/React.createElement(SettingsScreen, {
    account: data.account,
    onClose: () => setSettings(false)
  }) : null);
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  Phone
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/Phone.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/SettingsScreen.jsx
try { (() => {
// L-Phone — Settings window (NavigationView rail + panels).
function SettingsScreen({
  account,
  onClose
}) {
  const NS = window.LPhoneDesignSystem_5e292e;
  const {
    NavRail,
    NavRailItem,
    TextField,
    Select,
    ToggleSwitch,
    Button,
    StatusDot,
    Icon
  } = NS;
  const [section, setSection] = React.useState("account");
  const items = [{
    tag: "account",
    icon: "contact",
    color: "#8a2be2",
    label: "Аккаунт"
  }, {
    tag: "devices",
    icon: "microphone",
    color: "#1f8a3c",
    label: "Звонки и устройства"
  }, {
    tag: "contacts",
    icon: "people",
    color: "#ee82ee",
    label: "Контакты"
  }, {
    tag: "codecs",
    icon: "lock",
    color: "#c71585",
    label: "Кодеки и шифрование"
  }, {
    tag: "advanced",
    icon: "settings",
    color: "#0078d7",
    label: "Дополнительно"
  }, {
    tag: "about",
    icon: "info",
    color: "#2b88d8",
    label: "О программе"
  }];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: "absolute",
      inset: 0,
      zIndex: 80,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      background: "rgba(0,0,0,.35)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 660,
      height: 540,
      display: "flex",
      flexDirection: "column",
      background: "var(--surface-card)",
      borderRadius: 8,
      overflow: "hidden",
      boxShadow: "var(--shadow-dialog)"
    }
  }, /*#__PURE__*/React.createElement(WinTitleBar, {
    title: "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u2014 L-Phone",
    onClose: onClose
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      display: "flex"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 210,
      borderRight: "1px solid var(--stroke-divider)",
      background: "var(--surface-page)"
    }
  }, /*#__PURE__*/React.createElement(NavRail, null, items.map(it => /*#__PURE__*/React.createElement(NavRailItem, {
    key: it.tag,
    icon: it.icon,
    iconColor: it.color,
    label: it.label,
    selected: section === it.tag,
    onClick: () => setSection(it.tag)
  })))), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minWidth: 0,
      overflow: "auto",
      padding: "16px 18px"
    }
  }, section === "account" ? /*#__PURE__*/React.createElement(AccountPanel, {
    account: account,
    NS: NS
  }) : null, section === "devices" ? /*#__PURE__*/React.createElement(DevicesPanel, {
    NS: NS
  }) : null, section === "advanced" ? /*#__PURE__*/React.createElement(AdvancedPanel, {
    NS: NS
  }) : null, section === "about" ? /*#__PURE__*/React.createElement(AboutPanel, {
    NS: NS
  }) : null, section === "contacts" || section === "codecs" ? /*#__PURE__*/React.createElement(PlaceholderPanel, {
    section: section,
    NS: NS
  }) : null))));
}
function SectionTitle({
  children
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 22,
      fontWeight: "var(--fw-semibold)",
      marginBottom: 14
    }
  }, children);
}
function AccountPanel({
  account,
  NS
}) {
  const {
    TextField,
    Select,
    Button,
    StatusDot
  } = NS;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 460
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      justifyContent: "space-between",
      marginBottom: 14
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 22,
      fontWeight: "var(--fw-semibold)"
    }
  }, "\u0410\u043A\u043A\u0430\u0443\u043D\u0442"), /*#__PURE__*/React.createElement(Button, {
    variant: "accent"
  }, "\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C")), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 14,
      color: "var(--text-secondary)"
    }
  }, "SIP-\u0430\u0434\u0440\u0435\u0441"), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 18,
      marginBottom: 8
    }
  }, account.sip), /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 16
    }
  }, /*#__PURE__*/React.createElement(StatusDot, {
    status: "online",
    label: "\u041F\u043E\u0434\u043A\u043B\u044E\u0447\u0451\u043D"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 12
    }
  }, /*#__PURE__*/React.createElement(TextField, {
    label: "\u041E\u0442\u043E\u0431\u0440\u0430\u0436\u0430\u0435\u043C\u043E\u0435 \u0438\u043C\u044F",
    defaultValue: account.displayName
  }), /*#__PURE__*/React.createElement(TextField, {
    label: "\u041B\u043E\u0433\u0438\u043D",
    defaultValue: account.login
  }), /*#__PURE__*/React.createElement(TextField, {
    label: "\u041F\u0430\u0440\u043E\u043B\u044C",
    type: "password",
    defaultValue: "secret123"
  }), /*#__PURE__*/React.createElement(TextField, {
    label: "\u0418\u043C\u044F \u0441\u0435\u0440\u0432\u0435\u0440\u0430",
    defaultValue: account.server
  }), /*#__PURE__*/React.createElement(TextField, {
    label: "\u041F\u043E\u0440\u0442",
    defaultValue: account.port
  }), /*#__PURE__*/React.createElement(Select, {
    label: "\u0422\u0440\u0430\u043D\u0441\u043F\u043E\u0440\u0442",
    defaultValue: account.transport,
    options: [{
      value: "udp",
      label: "UDP"
    }, {
      value: "tcp",
      label: "TCP"
    }, {
      value: "tls",
      label: "TLS"
    }]
  }), /*#__PURE__*/React.createElement(TextField, {
    label: "\u0422\u0430\u0439\u043C\u0430\u0443\u0442 \u0440\u0435\u0433\u0438\u0441\u0442\u0440\u0430\u0446\u0438\u0438 (\u0441\u0435\u043A)",
    defaultValue: account.expires
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 6
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "destructive",
    block: true
  }, "\u0423\u0434\u0430\u043B\u0438\u0442\u044C \u0430\u043A\u043A\u0430\u0443\u043D\u0442"))));
}
function DevicesPanel({
  NS
}) {
  const {
    Select,
    ToggleSwitch
  } = NS;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 460
    }
  }, /*#__PURE__*/React.createElement(SectionTitle, null, "\u0417\u0432\u043E\u043D\u043A\u0438 \u0438 \u0443\u0441\u0442\u0440\u043E\u0439\u0441\u0442\u0432\u0430"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 14
    }
  }, /*#__PURE__*/React.createElement(ToggleSwitch, {
    label: "\u042D\u0445\u043E\u043F\u043E\u0434\u0430\u0432\u0438\u0442\u0435\u043B\u044C",
    defaultChecked: true
  }), /*#__PURE__*/React.createElement(Select, {
    label: "\u0421\u043F\u043E\u0441\u043E\u0431 \u043F\u0435\u0440\u0435\u0434\u0430\u0447\u0438 DTMF",
    defaultValue: "rfc2833",
    options: [{
      value: "rfc2833",
      label: "RFC2833 (RTP)"
    }, {
      value: "info",
      label: "SIP INFO"
    }]
  }), /*#__PURE__*/React.createElement(Select, {
    label: "\u041C\u0438\u043A\u0440\u043E\u0444\u043E\u043D",
    defaultValue: "default",
    options: [{
      value: "default",
      label: "Микрофон (Realtek Audio)"
    }, {
      value: "headset",
      label: "Гарнитура (USB)"
    }]
  }), /*#__PURE__*/React.createElement(Select, {
    label: "\u0414\u0438\u043D\u0430\u043C\u0438\u043A",
    defaultValue: "default",
    options: [{
      value: "default",
      label: "Динамики (Realtek Audio)"
    }, {
      value: "headset",
      label: "Гарнитура (USB)"
    }]
  }), /*#__PURE__*/React.createElement(Select, {
    label: "\u041A\u0430\u043C\u0435\u0440\u0430",
    defaultValue: "none",
    options: [{
      value: "none",
      label: "(нет камеры)"
    }, {
      value: "cam",
      label: "Integrated Webcam"
    }]
  })));
}
function AdvancedPanel({
  NS
}) {
  const {
    Select,
    ToggleSwitch,
    TextField,
    Button
  } = NS;
  const setTheme = v => {
    const root = document.documentElement;
    if (v === "dark") root.setAttribute("data-theme", "dark");else root.removeAttribute("data-theme");
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 460
    }
  }, /*#__PURE__*/React.createElement(SectionTitle, null, "\u0414\u043E\u043F\u043E\u043B\u043D\u0438\u0442\u0435\u043B\u044C\u043D\u043E"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 14
    }
  }, /*#__PURE__*/React.createElement(Select, {
    label: "\u041E\u0444\u043E\u0440\u043C\u043B\u0435\u043D\u0438\u0435",
    defaultValue: "light",
    onChange: setTheme,
    options: [{
      value: "system",
      label: "Системная"
    }, {
      value: "light",
      label: "Светлая"
    }, {
      value: "dark",
      label: "Тёмная"
    }]
  }), /*#__PURE__*/React.createElement(ToggleSwitch, {
    label: "\u0417\u0430\u043F\u0443\u0441\u043A\u0430\u0442\u044C \u0432\u043C\u0435\u0441\u0442\u0435 \u0441 Windows"
  }), /*#__PURE__*/React.createElement(ToggleSwitch, {
    label: "\u0421\u0432\u043E\u0440\u0430\u0447\u0438\u0432\u0430\u0442\u044C \u0432 \u0442\u0440\u0435\u0439 \u043F\u0440\u0438 \u0437\u0430\u043A\u0440\u044B\u0442\u0438\u0438",
    defaultChecked: true
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      fontWeight: "var(--fw-semibold)",
      marginTop: 6
    }
  }, "\u0421\u0435\u0442\u044C (NAT)"), /*#__PURE__*/React.createElement(TextField, {
    label: "STUN-\u0441\u0435\u0440\u0432\u0435\u0440",
    placeholder: "stun.example.com:3478"
  }), /*#__PURE__*/React.createElement(ToggleSwitch, {
    label: "\u0418\u0441\u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u044C ICE"
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "standard"
  }, "\u041F\u0440\u0438\u043C\u0435\u043D\u0438\u0442\u044C (\u0441\u0435\u0442\u044C)")));
}
function AboutPanel({
  NS
}) {
  const {
    Icon
  } = NS;
  const row = (icon, color, label, value) => /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: 22,
    color: color
  }), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 12,
      color: "var(--text-tertiary)"
    }
  }, label), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 14,
      fontWeight: "var(--fw-semibold)"
    }
  }, value)));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 460,
      display: "flex",
      flexDirection: "column",
      gap: 14
    }
  }, /*#__PURE__*/React.createElement(SectionTitle, null, "\u041E \u043F\u0440\u043E\u0434\u0443\u043A\u0442\u0435"), row("info", "#2b88d8", "Версия", "L-Phone 1.0"), row("completed-solid", "#2ba84a", "Лицензия", "GPLv3"), row("lock", "#2b88d8", "liblinphone SDK", "5.3.0"), row("settings", "#7e57c2", "Рантайм", "WinAppSDK 2.1.3 · .NET 10"));
}
function PlaceholderPanel({
  section,
  NS
}) {
  const title = section === "contacts" ? "Контакты" : "Кодеки и шифрование";
  const note = section === "contacts" ? "Внешняя адресная книга загружается по HTTP (XML вида PhoneBook → DirectoryEntry)." : "Приоритет аудио/видео кодеков и шифрование медиа (SRTP / ZRTP / DTLS).";
  return /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 460
    }
  }, /*#__PURE__*/React.createElement(SectionTitle, null, title), /*#__PURE__*/React.createElement("div", {
    style: {
      color: "var(--text-secondary)",
      fontSize: 13
    }
  }, note));
}
function WinTitleBar({
  title,
  onClose
}) {
  const {
    Icon
  } = window.LPhoneDesignSystem_5e292e;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      height: 36,
      flex: "none",
      background: "var(--surface-card)",
      borderBottom: "1px solid var(--stroke-divider)",
      paddingLeft: 12,
      justifyContent: "space-between"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      fontSize: 12,
      color: "var(--text-secondary)"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 16,
      height: 16,
      borderRadius: 3,
      background: "var(--surface-brand)",
      color: "#fff",
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      fontSize: 11,
      fontWeight: "var(--fw-semibold)"
    }
  }, "L"), title), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      height: "100%"
    }
  }, /*#__PURE__*/React.createElement(CaptionBtn, {
    glyph: "chrome-minimize"
  }), /*#__PURE__*/React.createElement(CaptionBtn, {
    glyph: "checkbox"
  }), /*#__PURE__*/React.createElement(CaptionBtn, {
    glyph: "chrome-close",
    danger: true,
    onClick: onClose
  })));
}
function CaptionBtn({
  glyph,
  danger,
  onClick
}) {
  const {
    Icon
  } = window.LPhoneDesignSystem_5e292e;
  const [hover, setHover] = React.useState(false);
  return /*#__PURE__*/React.createElement("button", {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    onClick: onClick,
    style: {
      width: 44,
      height: "100%",
      border: 0,
      cursor: "pointer",
      background: hover ? danger ? "#c42b1c" : "var(--lp-subtle-hover)" : "transparent",
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center"
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: glyph,
    size: 11,
    color: hover && danger ? "#ffffff" : "#5d5d5d"
  }));
}
window.LPhoneKit = Object.assign(window.LPhoneKit || {}, {
  SettingsScreen,
  WinTitleBar
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/SettingsScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/l-phone/data.js
try { (() => {
// Fake directory + call history for the L-Phone UI kit demo.
window.LPhoneData = function () {
  const contacts = [{
    initials: "ИП",
    name: "Иван Петров",
    sip: "sip:1001@sip.example.com",
    number: "1001"
  }, {
    initials: "АК",
    name: "Анна Кузнецова",
    sip: "+7 495 765-43-21",
    number: "74957654321"
  }, {
    initials: "СВ",
    name: "Сергей Волков",
    sip: "sip:1042@sip.example.com",
    number: "1042"
  }, {
    initials: "ЕМ",
    name: "Елена Морозова",
    sip: "+7 812 553-21-09",
    number: "78125532109"
  }, {
    initials: "ДН",
    name: "Дмитрий Новиков",
    sip: "sip:2200@sip.example.com",
    number: "2200"
  }, {
    initials: "ОС",
    name: "Ольга Соколова",
    sip: "+7 903 118-77-65",
    number: "79031187765"
  }, {
    initials: "РЗ",
    name: "Ресепшн (общий)",
    sip: "sip:0@sip.example.com",
    number: "0"
  }];
  const history = [{
    dir: "in",
    name: "+7 495 123-45-67",
    number: "74951234567",
    when: "сегодня · 14:32",
    dur: "2 мин 14 с",
    missed: false
  }, {
    dir: "out",
    name: "Сергей Волков",
    number: "1042",
    when: "сегодня · 11:08",
    dur: "—",
    missed: false
  }, {
    dir: "missed",
    name: "Анна Кузнецова",
    number: "74957654321",
    when: "вчера · 18:41",
    dur: "пропущенный",
    missed: true
  }, {
    dir: "in",
    name: "Ресепшн (общий)",
    number: "0",
    when: "вчера · 09:15",
    dur: "47 с",
    missed: false
  }, {
    dir: "out",
    name: "+7 812 553-21-09",
    number: "78125532109",
    when: "пн · 16:53",
    dur: "5 мин 02 с",
    missed: false
  }];
  const account = {
    displayName: "Иван Петров",
    initials: "ИП",
    sip: "sip:1001@sip.example.com",
    login: "1001",
    server: "sip.example.com",
    port: "5060",
    transport: "tls",
    expires: "3600"
  };
  return {
    contacts,
    history,
    account
  };
}();
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/l-phone/data.js", error: String((e && e.message) || e) }); }

__ds_ns.Avatar = __ds_scope.Avatar;

__ds_ns.Badge = __ds_scope.Badge;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.Icon = __ds_scope.Icon;

__ds_ns.IconButton = __ds_scope.IconButton;

__ds_ns.StatusDot = __ds_scope.StatusDot;

__ds_ns.SearchBox = __ds_scope.SearchBox;

__ds_ns.Select = __ds_scope.Select;

__ds_ns.TextField = __ds_scope.TextField;

__ds_ns.ToggleSwitch = __ds_scope.ToggleSwitch;

__ds_ns.ListRow = __ds_scope.ListRow;

__ds_ns.BottomNavTab = __ds_scope.BottomNavTab;

__ds_ns.BottomNavBar = __ds_scope.BottomNavBar;

__ds_ns.DialKey = __ds_scope.DialKey;

__ds_ns.DialPad = __ds_scope.DialPad;

__ds_ns.NavRailItem = __ds_scope.NavRailItem;

__ds_ns.NavRail = __ds_scope.NavRail;

})();
