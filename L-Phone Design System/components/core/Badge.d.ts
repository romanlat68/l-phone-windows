import * as React from "react";

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Colour tone. Default "neutral". */
  tone?: "neutral" | "accent" | "success" | "danger" | "navy";
}

export function Badge(props: BadgeProps): JSX.Element;
