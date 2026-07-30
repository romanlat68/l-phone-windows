import * as React from "react";

export interface BottomNavTabProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Fluent (MDL2) icon name. */
  icon: string;
  /** Tab label (e.g. "Набор"). */
  label: string;
  /** Active/selected state — takes the accent colour. */
  active?: boolean;
}

export function BottomNavTab(props: BottomNavTabProps): JSX.Element;

export interface BottomNavBarProps extends React.HTMLAttributes<HTMLDivElement> {}

export function BottomNavBar(props: BottomNavBarProps): JSX.Element;
