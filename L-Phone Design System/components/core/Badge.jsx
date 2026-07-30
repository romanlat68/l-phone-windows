import React from "react";

const TONES = {
  neutral: { bg: "var(--lp-subtle-hover)", fg: "var(--text-secondary)" },
  accent: { bg: "var(--accent-default)", fg: "#fff" },
  success: { bg: "var(--state-success)", fg: "#fff" },
  danger: { bg: "var(--state-danger)", fg: "#fff" },
  navy: { bg: "var(--surface-brand)", fg: "#fff" },
};

/**
 * Badge — small count/label pill. Use `dot` for a minimal count chip on
 * nav tabs (e.g. missed calls).
 */
export function Badge({ tone = "neutral", children, style, ...rest }) {
  const t = TONES[tone] || TONES.neutral;
  return (
    <span
      style={{
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
        ...style,
      }}
      {...rest}
    >
      {children}
    </span>
  );
}
