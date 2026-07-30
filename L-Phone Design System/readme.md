# L-Phone Design System

A design system for **L-Phone** — a desktop **SIP / VoIP softphone** for Windows, built with **WinUI 3** and the **Microsoft Fluent Design System** on top of the **liblinphone** SDK. The product is a compact, phone-shaped utility window: an account header, a dial pad, contacts and call history, in-call controls, and a Fluent settings window. The UI ships in **Russian**.

This system captures Fluent's foundations (neutrals, accent, type ramp, 4px grid, layered shadows) **plus L-Phone's own brand layer**: a dark navy identity, call-green / hang-up-red semantics, and Segoe Fluent iconography.

## Sources

| Source | Location | Notes |
|--------|----------|-------|
| L-Phone codebase | `winui/` (attached folder) | C# + WinUI 3 app. Key files: `LinphoneWinUI/MainWindow.xaml` (dialer, contacts, history, in-call bar, bottom nav) and `SettingsWindow.xaml` (NavigationView settings). |
| Platform | Microsoft WinUI 3 / WindowsAppSDK 2.1.3, .NET 10 | Fluent control library (`XamlControlsResources`). |
| SDK | liblinphone 5.x | SIP/VoIP engine. |
| Icon set | Segoe Fluent Icons (native) → Iconify `fluent-mdl2` (cross-platform) | See **Iconography**. |

> The reader is not assumed to have the codebase. Everything needed is captured here; the paths above are recorded in case they do.

---

## Content fundamentals

The product UI is **Russian**, terse, and functional — system-utility tone, not marketing.

- **Voice:** neutral and instructional. Tells the user the state of things ("Не подключён", "Идёт разговор", "На удержании") and what to do ("Откройте настройки для входа"). No exclamation, no personality, no first person.
- **Address:** impersonal / implied imperative. Buttons are bare verbs — "Принять", "Отклонить", "Сохранить", "Удалить аккаунт", "Очистить журнал". Settings actions add scope — "Сохранить и проверить", "Применить (сеть)".
- **Casing:** sentence case everywhere — labels, buttons, headings ("Звонки и устройства", "Кодеки и шифрование"). No Title Case, no ALL-CAPS. Field headers are sentence case ("Имя сервера", "Таймаут регистрации (сек)").
- **Status copy** is a short phrase, often with a trailing detail: "Удержано собеседником", "Core не запущен", "URL адресной книги не задан — укажите в ⚙ Настройки → Контакты."
- **Numbers & technical terms** stay literal and English where they're protocol terms: SIP, UDP/TCP/TLS, RFC2833, SRTP/ZRTP/DTLS, STUN, ICE, DTMF. Units are spelled in Russian ("сек", "мин").
- **Punctuation:** ellipsis "…" for in-progress ("Вызов…", "Выберите…"); em-dash "—" as an empty/placeholder value; "⚙" appears inline in help text.
- **Emoji:** none. The product never uses emoji. Iconography carries all symbolic meaning.

**Example strings (verbatim from the app):**
`Не подключён` · `Откройте настройки для входа` · `Входящий звонок` · `Идёт разговор` · `На удержании` · `Удержано собеседником` · `Принять` / `Отклонить` · `Микрофон` · `Удержание` · `Перевод` · `Сброс` · `Звонки и устройства` · `Кодеки и шифрование` · `Удалить аккаунт`

---

## Visual foundations

**Overall feel.** A small, dense, professional Windows utility. Light Fluent surfaces with a single saturated **navy** brand zone (the header and in-call bar). Calm, flat, slightly cool. No illustration, no photography, no gradients-as-decoration.

- **Color.** Light Fluent neutrals (near-white surfaces `#F3F3F3`/`#F9F9F9`/`#FFFFFF`, layered black inks at 0.89 / 0.61 / 0.45 opacity). One brand hue — **navy `#22334D`** — owns the header, in-call bar, avatars and dial-key numerals (`#4E6075`). Accent is **Fluent blue `#0078D7`** (primary buttons, selection, active tab, focus underline). Semantics are literal: **green `#2BA84A`** = accept / connected / call FAB, **red `#C42B1C`** = hang-up / delete. Settings rail icons are individually tinted (violet, green, magenta, blue) — a deliberate Fluent touch. A full **dark theme** is defined (`:root[data-theme="dark"]`).
- **Type.** **Segoe UI Variable** (native on Windows; Source Sans 3 is the cross-platform fallback). Standard Fluent ramp — Caption 12, Body 14, Body Strong 14/600, Subtitle 20/600, Title 28/600. Weights are Regular (400) and SemiBold (600); SemiBold marks headings and emphasis. App specials: 32px dial numerals, 24px number entry, 11px tab labels. Mono is **Cascadia / Consolas** (logs, SIP traces).
- **Spacing & layout.** 4px grid. The app is a fixed, narrow window (min 400×680). Three-zone vertical layout: header (Auto) / content (fills) / bottom nav (Auto), with the in-call bar slotting in above the nav. Content is centered and width-capped (number box `max-width: 250`, settings panels `max-width: 460`). The dial pad uses a fixed-gap grid so spacing doesn't drift with width.
- **Backgrounds.** Solid Fluent fills only — no images, textures, patterns or decorative gradients. The brand navy is a solid block. (The UI-kit demo page sits the window on a subtle neutral radial just for presentation; the product itself is flat.)
- **Corners.** 4px on controls (buttons, inputs, list rows), 8px on cards / flyouts / dialogs / the window itself, **pill** for the active-call capsule, **round** for avatars and all circular call controls.
- **Borders.** Hairline 1px. Cards use a near-transparent stroke (`rgba(0,0,0,.058)`); controls use a slightly stronger stroke with a **darker bottom edge** (`rgba(0,0,0,.45)`) — the signature Fluent control border. On focus the bottom edge becomes a **2px accent underline**.
- **Elevation.** Layered Fluent shadows, soft and low-contrast: `--shadow-card` (rest), `--shadow-flyout` (menus / Select), `--shadow-dialog` (Settings window). The call FAB carries a green glow. No hard or colored drop shadows elsewhere.
- **Motion.** Restrained and fast. Fluent easing (`cubic-bezier(.33,0,.67,1)`), ~100–167ms. Hover/press are background-fill swaps; the toggle knob slides; circular buttons nudge to `scale(.94)` on press. No bounce, no parallax, no looping/decorative animation.
- **States.** Hover = subtle dark fill wash (`rgba(0,0,0,.037)`) on light, translucent-white on navy; the close caption button flips to red. Press = a lighter fill + secondary text. Focus-visible = accent outline / 2px underline. Disabled = subtle fill + 0.36-opacity ink.
- **Transparency & blur.** Used sparingly and only on the navy zone — in-call controls are translucent white (`rgba(255,255,255,.20)`) over navy. No acrylic/blur in this recreation (the product relies on solid Mica-like fills).
- **Imagery vibe.** None. Identity is typographic + iconographic. The only "image" is the generated app mark: a white **"L"** on navy.

---

## Iconography

- **Native (production):** the app uses **Segoe Fluent Icons** (Win11) / **Segoe MDL2 Assets** (Win10) via `FontIcon Glyph="&#xE…;"`. Glyphs seen in the code include phone `E717`, contact `E716`, history `E81C`, microphone `E720`, video `E714`, pause/hold `E769`, transfer `E8AB`, decline-call `E778`, settings `E713`, info `E946`, encryption/lock `E943`, checkmark `E73E`, search `E721`, gripper `E76F`. Codepoints are documented on the **Iconography** card.
- **Cross-platform (this system):** Segoe Fluent Icons is a proprietary Windows font, so specimens and the UI kit render the **Iconify `fluent-mdl2`** set — Microsoft's own MDL2 icons published as SVG — through the `Icon` component. It requests `https://api.iconify.design/fluent-mdl2/<name>.svg?color=…` as an `<img>` (the colour is baked in because `<img>` can't inherit `currentColor`).
  - ⚠️ **Substitution flag:** this is a faithful visual match to Segoe MDL2, not the literal Windows font. For pixel-perfect Windows builds, use the native font + codepoints above. The cross-platform path also depends on the Iconify CDN.
- **Style:** single-weight line/solid hybrid, 16–24px, monochrome, tinted per context (navy default, white on the navy zone, accent for selection, semantic green/red on call controls). Settings rail icons are individually colored.
- **Unicode:** the app uses a few literal glyphs as icons — `⌫` (backspace), `∗` (dial star), `+` (under 0), `⚙` (inline in help text). The DS reproduces these via the `fluent-mdl2` equivalents (`backspace`, etc.) where appropriate.
- **No emoji**, ever.

---

## Index / manifest

**Root**
- `styles.css` — global entry point (import-only). Consumers link this.
- `tokens/colors.css` · `tokens/typography.css` · `tokens/spacing.css` — design tokens (base + semantic; light & dark).
- `readme.md` (this file) · `SKILL.md` (Agent-Skills wrapper).
- `_ds_bundle.js`, `_ds_manifest.json`, `_adherence.oxlintrc.json` — **generated**, do not edit.

**Foundation cards** (`guidelines/`, shown in the Design System tab)
- Colors: brand navy, accent, neutrals, semantic & call.
- Type: ramp, weights & dial numerals, mono.
- Spacing: scale, radius, elevation.
- Brand: app mark & wordmark, iconography.

**Components** (`components/`, namespace `window.LPhoneDesignSystem_*`)
- `core/` — `Icon`, `Button`, `IconButton`, `Avatar`, `StatusDot`, `Card`, `Badge`.
- `forms/` — `TextField`, `ToggleSwitch`, `Select`, `SearchBox`.
- `navigation/` — `BottomNavBar` + `BottomNavTab`, `NavRail` + `NavRailItem`, `DialPad` + `DialKey`.
- `lists/` — `ListRow`.
- Each directory has a `.d.ts` (props), `.prompt.md` (usage) and a `@dsCard` HTML specimen.

**UI kits** (`ui_kits/`)
- `l-phone/` — interactive softphone recreation (`index.html`). See its `README.md`.

**Starting points**
- `Button` (Core) — seed a new design with the button.

## Using this system
- Link `styles.css`; use the CSS custom properties (`--surface-brand`, `--accent-default`, `--call-accept`, `--text-primary`, the type role classes, etc.).
- In HTML cards, load `_ds_bundle.js` and read components from `window.LPhoneDesignSystem_5e292e` (run `check_design_system` to confirm the exact namespace).
- Build screens by composing primitives — don't re-implement them (see the UI kit).
