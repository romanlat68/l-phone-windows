import React from "react";

const COLORS = {
  online: "var(--state-success, #2ba84a)",
  offline: "var(--state-offline, #8a8a8a)",
  busy: "var(--state-danger, #c42b1c)",
  away: "var(--state-warning, #c98a14)",
  connecting: "var(--state-warning, #c98a14)",
};

/**
 * StatusDot — small filled circle for registration / call state, with an
 * optional inline label (e.g. "Не подключён").
 */
export function StatusDot({
  status = "offline",
  size = 10,
  label,
  labelColor = "var(--text-secondary)",
  style,
  ...rest
}) {
  return (
    <span
      style={{ display: "inline-flex", alignItems: "center", gap: 8, ...style }}
      {...rest}
    >
      <span
        style={{
          width: size,
          height: size,
          flex: "none",
          borderRadius: "var(--radius-round, 50%)",
          background: COLORS[status] || COLORS.offline,
        }}
      />
      {label != null ? (
        <span
          style={{
            fontFamily: "var(--font-sans)",
            fontSize: 13,
            color: labelColor,
          }}
        >
          {label}
        </span>
      ) : null}
    </span>
  );
}
