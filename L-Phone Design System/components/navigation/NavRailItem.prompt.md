Settings left navigation rail — Fluent NavigationView entries with tinted icons and a selected accent bar.

```jsx
<NavRail>
  <NavRailItem icon="contact" iconColor="#8a2be2" label="Аккаунт" selected />
  <NavRailItem icon="microphone" iconColor="#1f8a3c" label="Звонки и устройства" />
  <NavRailItem icon="lock" iconColor="#c71585" label="Кодеки и шифрование" />
</NavRail>
```

Each Settings item keeps its own icon tint (per the app); selection adds a subtle fill + 3px accent bar.
