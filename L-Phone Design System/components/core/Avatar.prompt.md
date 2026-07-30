Circular initials avatar with optional presence dot — used in the header (account) and contact/history rows.

```jsx
<Avatar initials="ИП" size={40} tone="slate" status="online" ringColor="#22334d" />
<Avatar initials="АК" size={38} tone="navy" />
```

Notes:
- On the navy header use `tone="slate"` and set `ringColor` to the navy surface so the status dot reads cleanly.
- `status`: online (green) / offline (gray) / busy (red) / away (amber) — mirrors the app's registration dot.
