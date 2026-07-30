import * as React from "react";

export interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Visual tone. Default "subtle". */
  variant?: "subtle" | "glass" | "accept" | "end" | "accent";
  /** Diameter in px. Default 48. Call FAB is 64; in-call controls 48. */
  size?: number;
  /** Fluent (MDL2) icon name. */
  icon: string;
  /** Override icon colour (defaults per variant). */
  iconColor?: string;
  /** Override icon size (defaults to ~42% of button size). */
  iconSize?: number;
  /** Accessible label / tooltip. */
  title?: string;
}

export function IconButton(props: IconButtonProps): JSX.Element;
