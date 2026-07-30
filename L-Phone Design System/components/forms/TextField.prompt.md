Fluent text input with header label — used throughout Settings (login, server, port, display name).

```jsx
<TextField label="Имя сервера" placeholder="sip.example.com" />
<TextField label="Порт" placeholder="5060" hint="По умолчанию 5060" />
<TextField type="password" label="Пароль" />
```

Notes:
- Shows the signature 2px accent underline on focus.
- `prefix`/`suffix` accept adornments (icons, clear buttons). `invalid` paints a red stroke.
