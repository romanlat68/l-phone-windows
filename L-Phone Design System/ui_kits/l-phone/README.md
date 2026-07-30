# L-Phone — UI kit

An interactive, high-fidelity recreation of the **L-Phone** softphone (WinUI 3 / Fluent), composed entirely from this design system's components.

## Run
Open `index.html`. It loads `_ds_bundle.js` (the compiled component library), `data.js` (fake directory + history), and the screen modules, then mounts `<Phone />`.

## What you can do
- **Dial** — tap the keypad, backspace, press the green call FAB → ringing → connected (a duration timer runs). The in-call bar (mute / hold / video / transfer / hang-up) appears above the tabs.
- **Switch tabs** — Набор / Контакты / Журнал (bottom nav, always visible, even mid-call).
- **Contacts** — search the external directory; tap a contact to load their number into the dialer.
- **History** — incoming / outgoing / missed calls; tap to redial.
- **Incoming call** — open the header ⚙ menu → "Симулировать входящий" → Принять / Отклонить.
- **Settings** — header ⚙ menu → "Настройки": NavigationView rail with Account, Devices, Advanced (the **Оформление → Тёмная** select flips the whole system to the dark theme), About, etc.

## Files
| File | Role |
|------|------|
| `index.html` | Window chrome + mounts `<Phone />` |
| `Phone.jsx` | App shell + call state machine |
| `Header.jsx` | Navy account header + overflow menu |
| `DialerScreen.jsx` | Number entry, dial pad, call pill, incoming panel |
| `InCallBar.jsx` | In-call circular controls |
| `ContactsScreen.jsx` | Directory search + list |
| `HistoryScreen.jsx` | Call log |
| `SettingsScreen.jsx` | Settings window (rail + panels) |
| `data.js` | Demo contacts / history / account |

## Source of truth
Recreated from `winui/LinphoneWinUI/MainWindow.xaml` and `SettingsWindow.xaml` (the C#/WinUI app). Component visuals come from the DS primitives — screens compose them, they don't re-implement them.

## Notes / cut corners
- No real SIP stack — call flow is simulated with timers.
- Audio-device selects show representative options, not live enumeration.
- Codecs and full Contacts-provisioning panels are stubbed with their real copy.
