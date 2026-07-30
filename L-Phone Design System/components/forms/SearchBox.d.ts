import * as React from "react";

export interface SearchBoxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "onSubmit"> {
  placeholder?: string;
  /** Fired on Enter or icon click with the current query. */
  onSubmit?: (query: string) => void;
}

export function SearchBox(props: SearchBoxProps): JSX.Element;
