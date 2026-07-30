import * as React from "react";

export interface StatusDotProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Registration / presence state. Default "offline". */
  status?: "online" | "offline" | "busy" | "away" | "connecting";
  /** Dot diameter in px. Default 10. */
  size?: number;
  /** Optional inline label. */
  label?: string;
  /** Label colour. Default secondary text. */
  labelColor?: string;
}

export function StatusDot(props: StatusDotProps): JSX.Element;
