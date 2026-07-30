import * as React from "react";

export interface ListRowProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Leading element — typically an <Avatar/> or <Icon/>. */
  leading?: React.ReactNode;
  /** Primary line (contact name / call target). */
  title?: React.ReactNode;
  /** Secondary line (number, call direction + time). */
  subtitle?: React.ReactNode;
  /** Trailing content (time, call-type icon). */
  trailing?: React.ReactNode;
}

export function ListRow(props: ListRowProps): JSX.Element;
