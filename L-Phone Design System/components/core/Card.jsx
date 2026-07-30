import React from "react";

/**
 * Card — Fluent surface container with hairline stroke and 8px radius.
 * Use `elevation` for flyout/dialog-level shadow.
 */
export function Card({
  elevation = "card",
  padding = 16,
  interactive = false,
  className = "",
  style,
  children,
  ...rest
}) {
  const shadow =
    elevation === "none"
      ? "none"
      : elevation === "flyout"
      ? "var(--shadow-flyout)"
      : elevation === "dialog"
      ? "var(--shadow-dialog)"
      : "var(--shadow-card)";
  return (
    <div
      className={className}
      style={{
        background: "var(--surface-card)",
        border: "1px solid var(--stroke-card)",
        borderRadius: "var(--radius-md, 8px)",
        boxShadow: shadow,
        padding,
        boxSizing: "border-box",
        cursor: interactive ? "pointer" : undefined,
        transition: interactive ? "background var(--dur-fast,.1s)" : undefined,
        ...style,
      }}
      {...rest}
    >
      {children}
    </div>
  );
}
