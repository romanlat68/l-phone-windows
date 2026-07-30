Fluent (MDL2) icon — use anywhere the L-Phone UI needs a glyph; names come from the Iconify `fluent-mdl2` set.

```jsx
<Icon name="phone" size={20} color="#ffffff" />
<Icon name="decline-call" size={20} color="#c42b1c" title="Hang up" />
```

Notes:
- `color` is baked into the SVG (an `<img>` can't inherit `currentColor`), so always pass the colour you want; default is brand navy `#22334d`.
- Common names: `phone`, `contact`, `history`, `microphone`, `video`, `pause`, `switch`, `decline-call`, `settings`, `info`, `lock`, `completed-solid`, `search`, `gripper-bar-horizontal`, `more`, `add-friend`.
- On Windows production builds, prefer the native "Segoe Fluent Icons" font with the matching codepoint (see the Iconography card) for crispness and offline use.
