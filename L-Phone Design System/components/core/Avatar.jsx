import React from "react";

const TONES = {
  navy: { bg: "var(--surface-brand, #22334d)", fg: "#ffffff" },
  slate: { bg: "var(--lp-slate-100, #cbd3dc)", fg: "var(--surface-brand, #22334d)" },
  accent: { bg: "var(--accent-default, #0078d7)", fg: "#ffffff" },
};

const STATUS = {
  online: "var(--state-success, #2ba84a)",
  offline: "var(--state-offline, #8a8a8a)",
  busy: "var(--state-danger, #c42b1c)",
  away: "var(--state-warning, #c98a14)",
};

/**
 * Avatar — circular initials badge with an optional registration/presence dot.
 */
export function Avatar({
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
  return (
    <span
      style={{
        position: "relative",
        display: "inline-flex",
        width: size,
        height: size,
        flex: "none",
        ...style,
      }}
      {...rest}
    >
      <span
        style={{
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
          userSelect: "none",
        }}
      >
        {initials}
      </span>
      {status ? (
        <span
          style={{
            position: "absolute",
            right: 0,
            bottom: 0,
            width: dot,
            height: dot,
            borderRadius: "var(--radius-round, 50%)",
            background: STATUS[status] || STATUS.offline,
            border: `2px solid ${ringColor}`,
            boxSizing: "border-box",
          }}
        />
      ) : null}
    </span>
  );
}
