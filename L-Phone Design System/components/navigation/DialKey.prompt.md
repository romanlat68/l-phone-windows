Dial-pad key and the full 3×4 keypad — the centrepiece of the Набор (dialer) screen.

```jsx
<DialPad onPress={(c) => setNumber(n => n + c)} />

// or a single key:
<DialKey digit="0" sub="+" onPress={append} />
```

Notes:
- Numerals use brand navy-400; sub-line (letters / "+") is tertiary text.
- Fixed-gap grid so spacing doesn't shift with container width (matches the app).
