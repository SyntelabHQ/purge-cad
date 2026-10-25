# Localization

PurgeCAD is available in 62 languages, listed in a fixed order in `src/PurgeCad.Localization/Languages.cs`
(the same order as on syntelab.com).

## Files

- `src/PurgeCad.Localization/Strings/en.json` — English, the **source** and the fallback for missing keys.
- `src/PurgeCad.Localization/Strings/<code>.json` — one file per language (`ro.json`, `pt-BR.json`, `zh-TW.json`, …).

Each file is a flat JSON object of `"key": "text"`. Keys are dot-separated (`main.start`, `engine.acadsharp.risk`).

```json
{
  "main.start": "Start",
  "scan.found": "Found: {dwg} DWG · {dxf} DXF"
}
```

## Rules

- Keep placeholders such as `{dwg}` unchanged.
- Do not translate file extensions, `AutoCAD`, `ACadSharp`, `PurgeCAD`, `Syntelab`.
- Plurals: use the keys `.one` / `.other` (and `.few` / `.many` where the language needs them, following CLDR).
- Right-to-left languages (ar, fa, ur, he) are flipped automatically; no special text is needed.
- A missing key falls back to English; a test checks that every file has every key.

## Language picker

The first entry is "System language" (the OS language if it is one of the 62, otherwise English),
followed by the 62 languages in the fixed order. Search matches the native name, the English name and the code.
