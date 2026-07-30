import * as React from "react";

export interface TextFieldProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "prefix"> {
  /** Header label above the field. */
  label?: string;
  /** Helper text below the field. */
  hint?: string;
  /** Marks the field invalid (red stroke). */
  invalid?: boolean;
  /** Leading adornment (e.g. an <Icon/>). */
  prefix?: React.ReactNode;
  /** Trailing adornment (e.g. a clear button). */
  suffix?: React.ReactNode;
}

export function TextField(props: TextFieldProps): JSX.Element;
