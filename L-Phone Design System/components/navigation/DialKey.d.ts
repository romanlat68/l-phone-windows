import * as React from "react";

export interface DialKeyProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, "onClick"> {
  /** The numeral / symbol shown large. */
  digit: string;
  /** Sub-line under the digit (letters, or "+"). */
  sub?: string;
  /** Fired with the digit when pressed. */
  onPress?: (digit: string) => void;
}

export function DialKey(props: DialKeyProps): JSX.Element;

export interface DialPadProps {
  /** Fired with each pressed character. */
  onPress?: (char: string) => void;
  className?: string;
  style?: React.CSSProperties;
}

export function DialPad(props: DialPadProps): JSX.Element;
