Fluent push button — the primary action control. Use `accent` for the single primary action on a surface, `standard` for secondary actions, `subtle` for low-emphasis/toolbar actions, `destructive` for delete/remove.

```jsx
<Button variant="accent" onClick={save}>Сохранить</Button>
<Button variant="standard" icon="phone">Позвонить</Button>
<Button variant="subtle" size="sm">Отмена</Button>
<Button variant="destructive">Удалить аккаунт</Button>
```

Notes:
- `block` stretches to full width (common in narrow dialer panels).
- Pair with `icon` for a leading glyph; `iconColor` overrides the default per-variant tone.
- Only one `accent` button per view — Fluent treats it as the default action.
