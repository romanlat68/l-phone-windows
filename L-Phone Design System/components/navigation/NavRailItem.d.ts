import * as React from "react";

export interface NavRailItemProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Fluent (MDL2) icon name. */
  icon: string;
  /** Icon colour (Settings items are individually tinted). */
  iconColor?: string;
  /** Item label (e.g. "Аккаунт"). */
  label: string;
  /** Selected state — subtle fill + accent bar. */
  selected?: boolean;
}

export function NavRailItem(props: NavRailItemProps): JSX.Element;

export interface NavRailProps extends React.HTMLAttributes<HTMLElement> {}

export function NavRail(props: NavRailProps): JSX.Element;
