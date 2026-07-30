import * as React from "react";

/**
 * @startingPoint section="Core" subtitle="Fluent push button — standard, accent, subtle & destructive" viewport="700x150"
 */
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Visual style. Default "standard". */
  variant?: "standard" | "accent" | "subtle" | "destructive";
  /** Control height. Default "md" (32px); "sm" is 28px. */
  size?: "md" | "sm";
  /** Stretch to full container width. */
  block?: boolean;
  /** Leading Fluent (MDL2) icon name. */
  icon?: string;
  /** Override the icon colour (defaults to a sensible per-variant tone). */
  iconColor?: string;
}

export function Button(props: ButtonProps): JSX.Element;
