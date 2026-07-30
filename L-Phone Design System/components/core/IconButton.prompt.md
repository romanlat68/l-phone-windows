Circular icon-only button — the dialer's call/hang-up FABs and in-call controls (mute, hold, video, transfer), plus subtle header actions.

```jsx
<IconButton variant="accept" size={64} icon="phone" title="Позвонить" />
<IconButton variant="end" size={48} icon="decline-call" title="Сброс" />
<IconButton variant="glass" icon="microphone" title="Микрофон" />
<IconButton variant="subtle" size={36} icon="settings" title="Меню" />
```

Notes:
- `glass` is the translucent-white control used on the navy in-call bar.
- `accept` carries a green glow shadow; `end` is the red hang-up.
- Press state nudges scale to 0.94. Always pass `title` for accessibility.
