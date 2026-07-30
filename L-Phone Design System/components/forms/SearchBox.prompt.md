Fluent search input with a trailing Find icon — Contacts search ("Поиск по имени или номеру").

```jsx
<SearchBox placeholder="Поиск по имени или номеру" onChange={e => filter(e.target.value)} />
```

Fires `onSubmit(query)` on Enter or icon click.
