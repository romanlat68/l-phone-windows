List row for Contacts and Call History — leading avatar/icon, title, subtitle, optional trailing.

```jsx
<ListRow
  leading={<Avatar initials="ИП" size={38} tone="navy" />}
  title="Иван Петров"
  subtitle="sip:1001@sip.example.com"
/>
<ListRow
  leading={<Icon name="incoming-call" size={18} color="#2ba84a" />}
  title="+7 495 123-45-67"
  subtitle="Входящий · 14:32"
  trailing="2 мин"
/>
```
