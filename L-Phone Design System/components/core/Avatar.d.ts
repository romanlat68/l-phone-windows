import * as React from "react";

export interface AvatarProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Initials to display (1–2 chars). Default "—". */
  initials?: string;
  /** Diameter in px. Default 40. */
  size?: number;
  /** Fill tone. Default "slate". */
  tone?: "navy" | "slate" | "accent";
  /** Optional presence/registration dot. */
  status?: "online" | "offline" | "busy" | "away";
  /** Colour of the dot's surrounding ring (match the surface behind it). */
  ringColor?: string;
}

export function Avatar(props: AvatarProps): JSX.Element;
