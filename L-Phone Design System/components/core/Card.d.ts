import * as React from "react";

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Shadow depth. Default "card". */
  elevation?: "none" | "card" | "flyout" | "dialog";
  /** Inner padding in px. Default 16. */
  padding?: number;
  /** Adds pointer affordance for clickable cards. */
  interactive?: boolean;
}

export function Card(props: CardProps): JSX.Element;
