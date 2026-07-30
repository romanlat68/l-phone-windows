---
name: l-phone-design
description: Use this skill to generate well-branded interfaces and assets for L-Phone (a Windows WinUI 3 / Fluent SIP softphone), either for production or throwaway prototypes/mocks/etc. Contains essential design guidelines, colors, type, fonts, assets, and UI kit components for prototyping.
user-invocable: true
---

Read the `readme.md` file within this skill, and explore the other available files.

If creating visual artifacts (slides, mocks, throwaway prototypes, etc), copy assets out and create static HTML files for the user to view. If working on production code, you can copy assets and read the rules here to become an expert in designing with this brand.

If the user invokes this skill without any other guidance, ask them what they want to build or design, ask some questions, and act as an expert designer who outputs HTML artifacts _or_ production code, depending on the need.

## Map
- `readme.md` — full design guide: context, content fundamentals, visual foundations, iconography, file index.
- `styles.css` — link this one file to get all tokens + fonts. Everything is CSS custom properties (`--surface-brand`, `--accent-default`, `--call-accept`, `--text-primary`, type role classes like `.lp-title`).
- `tokens/` — colors (light + dark), typography (Segoe UI Variable ramp), spacing / radius / elevation / motion.
- `components/` — React primitives: `core/` (Icon, Button, IconButton, Avatar, StatusDot, Card, Badge), `forms/` (TextField, ToggleSwitch, Select, SearchBox), `navigation/` (BottomNavBar/Tab, NavRail/Item, DialPad/Key), `lists/` (ListRow). Each has a `.d.ts` (props) and `.prompt.md` (usage).
- `ui_kits/l-phone/` — interactive softphone recreation; the reference for how screens compose primitives.
- `guidelines/` — visual specimen cards.

## Quick rules
- Brand: navy `#22334D` header/in-call zone; Fluent accent blue `#0078D7`; green `#2BA84A` = accept, red `#C42B1C` = hang-up.
- Type: Segoe UI Variable (native on Windows; Source Sans 3 fallback). Sentence case. Russian, terse, no emoji.
- Icons: Segoe Fluent Icons on Windows; cross-platform via Iconify `fluent-mdl2` SVGs (the `Icon` component). Never hand-draw icons.
- Corners 4px (controls) / 8px (cards) / pill / round. Hairline borders with a darker bottom edge; 2px accent underline on focus. Soft layered Fluent shadows. Fast, restrained motion.

## In standalone HTML artifacts
Load React + Babel, then `_ds_bundle.js`, then read components from `window.LPhoneDesignSystem_<hash>` (the namespace is printed by the build; check `_ds_manifest.json` or the readme). For static specimens you can also just link `styles.css` and use the tokens directly.
