# Fonts (SIL Open Font License 1.1)

| File | Family name used in XAML | Use | License |
|---|---|---|---|
| `Sansation-Regular.ttf`, `Sansation-Bold.ttf` | Sansation | app name only | `OFL-Sansation.txt` |
| `NotoSans-Regular.ttf`, `-Medium`, `-SemiBold`, `-Bold` (static instances cut from `NotoSans[wdth,wght].ttf` with fontTools, so every platform renders the same weights) | Noto Sans | default UI font: Latin, Greek, Cyrillic, Vietnamese | `OFL-NotoSans.txt` |

Other scripts (Arabic, Hebrew, Indic, Thai, Myanmar, Ethiopic, Javanese, CJK) fall back to the
operating system's fonts. Bundling the matching Noto script fonts here is a later step; add each one
with its OFL file and register it as a fallback in `Program.cs`.

Source: https://github.com/google/fonts
