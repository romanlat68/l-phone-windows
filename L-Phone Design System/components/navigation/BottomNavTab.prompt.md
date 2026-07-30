Bottom navigation bar + tabs — the app's primary navigation (Набор / Контакты / Журнал). Always visible, even during a call.

```jsx
<BottomNavBar>
  <BottomNavTab icon="phone" label="Набор" active />
  <BottomNavTab icon="contact" label="Контакты" />
  <BottomNavTab icon="history" label="Журнал" />
</BottomNavBar>
```

Active tab uses the Fluent accent; inactive is gray.
