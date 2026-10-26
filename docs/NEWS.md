# Messages to users (news.json)

PurgeCAD reads `news.json` from the `main` branch of this repository when it starts
(`https://raw.githubusercontent.com/SyntelabHQ/purge-cad/main/news.json`). Nothing about the user is sent.
The newest message the user has not closed shows at the top of the main window; all current messages are in
About → Messages. Push a change to `news.json` and every copy sees it on its next start. No release needed.

```json
{
  "messages": [
    {
      "id": "2026-11-convert",
      "date": "2026-11-01",
      "title": { "en": "New: the Convert tab", "ro": "Nou: tabul Conversie" },
      "text":  { "en": "Save drawings in an older DWG / DXF version without purging them.",
                 "ro": "Salvează desenele într-o versiune DWG / DXF mai veche, fără să le purjezi." },
      "link": "https://syntelab.com/apps/purge-cad",
      "level": "info",
      "minVersion": "1.1.0",
      "maxVersion": "1.9.9",
      "until": "2026-12-31",
      "languages": ["en", "ro"]
    }
  ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Unique and never reused: a closed message stays closed by its id. |
| `title` | yes | Text, or texts by language code. English is used when the user's language has none. |
| `text` | no | Same as `title`. |
| `date` | no | `yyyy-MM-dd`, newest first. |
| `link` | no | Opened by "Read more". |
| `level` | no | `info` (blue, default) or `warning` (yellow). |
| `minVersion`, `maxVersion` | no | Only these PurgeCAD versions see it (for example a warning about a bug in 1.1.0). |
| `until` | no | Not shown after this day. |
| `languages` | no | Only users with one of these languages see it. |

To try a file before pushing it, start PurgeCAD with the environment variable `PURGECAD_NEWS` set to the file's path.
