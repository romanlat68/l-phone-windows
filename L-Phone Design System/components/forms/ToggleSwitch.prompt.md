Fluent on/off switch — the primary settings control (echo cancel, autostart, minimize-to-tray, codec enable).

```jsx
<ToggleSwitch label="Эхоподавитель" defaultChecked />
<ToggleSwitch label="Запускать вместе с Windows" checked={auto} onChange={setAuto} />
```

Notes:
- Accent fill + white knob when on; knob slides on toggle.
- Optional `onText`/`offText` render inline state copy beside the track.
