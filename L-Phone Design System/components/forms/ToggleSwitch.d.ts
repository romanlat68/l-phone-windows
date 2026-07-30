import * as React from "react";

export interface ToggleSwitchProps {
  /** Header label above the switch. */
  label?: string;
  /** Controlled on/off state. */
  checked?: boolean;
  /** Uncontrolled initial state. */
  defaultChecked?: boolean;
  /** Fired with the next value when toggled. */
  onChange?: (checked: boolean) => void;
  /** Inline text shown when on (e.g. "Вкл"). */
  onText?: string;
  /** Inline text shown when off (e.g. "Выкл"). */
  offText?: string;
  disabled?: boolean;
  className?: string;
  style?: React.CSSProperties;
}

export function ToggleSwitch(props: ToggleSwitchProps): JSX.Element;
