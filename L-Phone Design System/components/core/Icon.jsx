import React from "react";

/**
 * Icon — renders a Microsoft Fluent (MDL2) glyph.
 *
 * Cross-platform path: an <img> pointing at the Iconify `fluent-mdl2`
 * SVG endpoint (Microsoft's own MDL2 icon set), with the colour baked
 * into the request. On Windows production builds, prefer the native
 * "Segoe Fluent Icons" font with the matching codepoint instead.
 *
 * Colour can't be inherited through <img>, so pass an explicit `color`
 * (any CSS hex). Defaults to brand navy.
 */
export function Icon({
  name,
  size = 16,
  color = "#22334d",
  title,
  style,
  className,
  ...rest
}) {
  const encoded = encodeURIComponent(color);
  const src =
    `https://api.iconify.design/fluent-mdl2/${name}.svg` +
    `?color=${encoded}&width=${size}&height=${size}`;
  return (
    <img
      src={src}
      width={size}
      height={size}
      alt={title || name}
      aria-hidden={title ? undefined : true}
      className={className}
      style={{ display: "inline-block", flex: "none", ...style }}
      {...rest}
    />
  );
}
