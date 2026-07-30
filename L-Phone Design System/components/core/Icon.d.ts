import * as React from "react";

export interface IconProps extends Omit<React.ImgHTMLAttributes<HTMLImageElement>, "color"> {
  /** Iconify `fluent-mdl2` icon name, e.g. "phone", "decline-call", "settings". */
  name: string;
  /** Pixel size (square). Default 16. */
  size?: number;
  /** CSS hex colour baked into the SVG request. Default brand navy #22334d. */
  color?: string;
  /** Accessible label. When omitted the icon is treated as decorative. */
  title?: string;
}

/**
 * Fluent (MDL2) icon rendered from the Iconify CDN.
 */
export function Icon(props: IconProps): JSX.Element;
