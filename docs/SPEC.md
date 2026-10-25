# PurgeCAD — Product specification

**Status:** draft v0.1 (25 Oct 2026) · **Owner:** Syntelab · **License:** GPLv3 with additional attribution terms (see `ADDITIONAL-TERMS.md`)

PurgeCAD is a free, open-source desktop app by **Syntelab** that scans a folder, finds every DWG and DXF drawing in it,
and purges them in batch. It also cleans up the auxiliary files CAD programs leave behind (`.dwl`, `.dwl2`, `.bak`, `.sv$`).

- Official page: `https://syntelab.com/apps/purge-cad` (English), `https://syntelab.com/apps/<lang>/purge-cad` (other languages)
- Part of **Syntelab Apps** (`https://syntelab.com/apps`)
- Runs on **Windows, Linux and macOS**. Builds for all three come from GitHub Actions.
- 62 interface languages, chosen at first run and changeable at any time.

---

## 1. Goals and non-goals

**Goals**
1. Pick a folder (optionally with subfolders), see live how many DWG / DXF files are found, purge them all.
2. Never lose user data: copy mode by default, backups, dry-run, verification, clear report.
3. Let the user choose the purge engine and see its risks before running.
4. Separate tool that only cleans auxiliary files, without touching drawings.
5. Modern, simple UI; fully tokenized styling; modular code that can be extended (new engines, new batch operations).

**Non-goals (v1)**
- Editing drawings, viewing them, or converting between versions (may come later as extra batch operations).
- AUDIT/repair as a standalone tool (only "audit before purge" with the AutoCAD engine).
- Cloud features, accounts, telemetry. Everything runs locally, like every Syntelab tool.

## 2. Users and platforms

Anyone who works with DWG / DXF: surveyors, architects, engineers, drafters. Many will have **AutoCAD LT or no AutoCAD at all**,
and many will be on macOS or Linux. The app must be fully usable without AutoCAD.

| | Windows 10/11 x64 / arm64 | macOS 12+ (x64, Apple Silicon) | Linux x64 / arm64 |
|---|---|---|---|
| ACadSharp engine | yes | yes | yes |
| AutoCAD engine | yes, if full AutoCAD is installed | no | no |
| Recycle bin | Recycle Bin | Trash | freedesktop Trash |
| Package | single `.exe` + installer | `.app` in `.dmg` | AppImage / `.tar.gz` |

Packages are self-contained: nothing else (no .NET) needs to be installed.

## 3. Technology

| Part | Choice | Why |
|---|---|---|
| Language / runtime | **C# on .NET 10 (LTS)** | The only ecosystem with a free, mature DWG read/write library |
| UI | **Avalonia 11.3** (MIT) | One UI for Windows, macOS, Linux; right-to-left support; XAML styles = tokens. Upgrade to Avalonia 12 after the first tested build |
| MVVM | CommunityToolkit.Mvvm (MIT) | Small, source-generated view models |
| DWG / DXF engine | **ACadSharp 3.8** (MIT) | Reads DWG R14–2018+ and DXF, writes DWG (except 2007) and DXF |
| AutoCAD engine | `accoreconsole.exe` + a small plugin DLL | Windows only; AutoCAD itself writes the file |
| Tests | xUnit | |
| CI | GitHub Actions | builds, tests, packages for 3 OSes |

Rust was considered and rejected: there is no DWG library for Rust (only DXF), and LibreDWG (C, GPL) cannot yet write DWG reliably.

## 4. Purge engines

The engine is chosen in the main window, with its risk shown next to it. Each file's report row says which engine processed it.

| | **AutoCAD** | **ACadSharp** | **Automatic** |
|---|---|---|---|
| Available | Windows + full AutoCAD (not LT) | everywhere | everywhere |
| Who writes the file | AutoCAD | ACadSharp (rewrites the whole file) | AutoCAD where available, else ACadSharp |
| Risk | **Very low** | **Medium** | as per engine used |
| Known limits | cannot open files newer than the installed AutoCAD; Windows only | unknown objects (proxy objects, third-party / vertical app data such as Civil 3D) may be lost; cannot write DWG 2007 (AC1021) | — |

When the AutoCAD engine is not available, its option is disabled with the reason shown ("AutoCAD not found", "Not available on macOS").

### 4.1 What gets purged (both engines, same options)

Categories, defaults and what is always kept:

| Category | Default | Notes |
|---|---|---|
| Blocks | on | not layouts, xrefs or xref-dependent; nested blocks via repeated passes |
| Layers | on | not `0`, `Defpoints`, the current layer |
| Linetypes | on | not ByLayer, ByBlock, Continuous, the current one |
| Text styles, dimension styles | on | not the current ones |
| Table, multileader, multiline styles | on | not the current ones |
| Materials, detail / section view styles | on | AutoCAD engine only where ACadSharp lacks the object |
| Registered applications | on | not `ACAD` |
| Layer filters | on | layer states are kept |
| DGN linetype components | on | AutoCAD engine; ACadSharp if supported |
| Unused annotation scales | on | keep current and 1:1 |
| Zero-length lines/polylines, empty texts | **off** | "dangerous" options, off by default |

Purging repeats until a pass removes nothing (max 50 passes).

### 4.2 ACadSharp safeguards (all on by default)
1. **Pre-check:** if the file contains unknown/proxy objects, skip it with reason "contains objects ACadSharp does not understand" (option: process anyway).
2. **Write to temp, verify, then replace:** write `<name>.purgecad.tmp`, re-read it with ACadSharp, compare entity counts per space and block count minus purged; if anything differs, discard and report.
3. **Keep version:** save in the original version; AC1021 (2007) → skip, or save as 2010 (AC1024) if the user allows.
4. **DXF stays DXF** (ASCII or binary as original).

### 4.3 AutoCAD engine
- Finds AutoCAD automatically (registry, newest first) or a path chosen by the user.
- Runs 1–4 `accoreconsole` processes in parallel (default 2), each with its own file list.
- A plugin DLL (`PurgeCad.AutoCad.Plugin`, command `PURGECADRUN`) is loaded in accoreconsole with NETLOAD (SECURELOAD is turned off for the load and restored) and purges on `Database`: `Database.Purge` repeated until a pass removes nothing, DGN line style components no linetype reaches (then the DGN data of unused DGN linetypes, so they purge too), layer filters, unused annotation scales, and optionally zero-length geometry and empty texts. It reads its job file from the `PURGECAD_JOB` environment variable and writes counts back. If AutoCAD does not load or run it, the file is purged again with the plain script (-PURGE passes and AutoLISP).
- Per-file timeout; if accoreconsole hangs or crashes, it is restarted and the file is reported.
- Optional "AUDIT before purge".
- Two plugin builds: .NET Framework 4.6.2 (AutoCAD 2013–2024, compiled against the AutoCAD 2015 API from NuGet; AutoCAD's managed DLLs are not strong-named, so it also binds to 2013/2014) and .NET 8 (AutoCAD 2025+). Both are copied to `acad-plugin\<framework>\` next to PurgeCAD.exe by the app build.

## 5. Working modes and safety

- **Output mode** (radio buttons):
  - *Copy to another folder* (default): keeps the subfolder structure; originals untouched.
  - *In place, with backup*: original first copied to `<root>/_purgecad-backup/<yyyy-MM-dd_HHmm>/…`.
  - *Report only (dry-run)*: shows what would be removed, saves nothing.
- **Skipped files, with reason:** read-only, locked / open in a CAD program, 0 bytes, unreadable/corrupt, version too new, unknown objects (ACadSharp), DWG 2007 not writable (ACadSharp).
- **Atomic writes:** temp file next to the original, then replace. A crash never leaves a half-written drawing.
- **Xrefs:** never loaded or modified; each drawing is purged on its own.
- **Stop:** finishes the current file(s), then stops.
- **Cloud folders:** a note recommends pausing Dropbox / OneDrive sync during large runs.

## 6. Auxiliary file cleanup

Available **as an option in a purge run** and **as a separate tool** ("Clean auxiliary files") that never touches drawings.

| Type | Default | Rule |
|---|---|---|
| `.dwl`, `.dwl2` (lock files, often hidden) | off | deleted **only if** the matching drawing is not open (checked by trying an exclusive open). Otherwise skipped: "drawing is open by …" (user/machine read from the `.dwl`) |
| `.bak` | off | AutoCAD's only automatic backup; separate checkbox with a warning |
| `.sv$` (autosave) | off | separate checkbox |

- Before deleting: a confirmation with the list, count and total size.
- Files go to the **Recycle Bin / Trash**. On network drives (no recycle bin) they are moved to `<root>/_purgecad-deleted/<yyyy-MM-dd_HHmm>/…`, and the report says so.
- "Delete permanently" is a separate, off-by-default option.

## 7. User interface

**Style:** modern, calm, dark-first like syntelab.com/apps. Every colour, size, radius, spacing and font is a **token** (`src/PurgeCad.App/Themes/Tokens.axaml`); light and dark themes only remap tokens. No hard-coded colours in views.

**Brand colour — dual brown:**

| Token | Light theme | Dark theme |
|---|---|---|
| `Brand` | `#3C271D` | `#C59077` |
| `BrandHover` | `#563829` | `#D0A18B` |
| `BrandPressed` | `#291B14` | `#BA7C5E` |
| `OnBrand` (text on brand) | `#FFFFFF` | `#1A120B` |
| `BrandSubtle` (backgrounds) | `#F3EBE7` | `#2E2019` |

Same hue (19°) in both themes. Contrast: `#3C271D` on white 14:1, `#C59077` on `#121212` 6.8:1 (WCAG AA ≥ 4.5:1). If `#3C271D` reads as near-black on small elements (progress bar, checkboxes), use `BrandHover` there.

**Fonts:**
- **Sansation** (Google Fonts, OFL) for the app name "PurgeCAD" only (and big titles in Latin scripts).
- **Noto Sans** family (OFL) for everything else, with per-script fallbacks bundled: Latin/Greek/Cyrillic, Arabic, Hebrew, Devanagari, Bengali, Gujarati, Gurmukhi, Kannada, Malayalam, Tamil, Telugu, Thai, Myanmar, Ethiopic, Javanese.
- Chinese, Japanese, Korean use the system font (Microsoft YaHei / JhengHei / Yu Gothic / Malgun Gothic, PingFang / Hiragino / Apple SD Gothic, Noto CJK on Linux) to keep the package small.

**Icon:** `assets/icon/purge-cad.svg` (Font Awesome Free "brush", CC BY 4.0, credited in About and README), coloured with the brand brown. `.ico`, `.icns`, `.png` are generated from it.

**First run:** a small welcome screen: language (searchable list) + theme (Dark / Light). Saved, changeable later.

**Main window** (single window, top to bottom):
1. Header: icon + "PurgeCAD" (Sansation), language picker, theme toggle (one button, sun/moon), About.
2. Source: folder picker (also drag & drop), "Include subfolders", filter DWG / DXF.
3. Scan result: live counter "Found: 1 234 DWG · 87 DXF · 312 auxiliary files".
4. Options (collapsible): engine (with risk badge), output mode, what to purge, auxiliary cleanup, parallelism.
5. Start / Stop + progress bar "312 / 1 321 · ~4 min left".
6. Results table: file, status, size before → after, removed items, engine, time. Filter by status.
7. Summary at the end: purged, skipped, errors, space saved; "Save report" (CSV / TXT), "Open output folder".

Separate tool: "Clean auxiliary files" as a second tab with the same folder picker and its own run.

**Language picker:** searchable list like the one on syntelab.com: first entry "System language", then the 62 languages
in the fixed order below, each with native name, English name and code. Search matches native name, English name and code.

**Right-to-left:** Arabic, Persian, Urdu, Hebrew switch the whole layout to RTL (`FlowDirection`).

## 8. Languages (fixed order)

1 en English · 2 zh-CN 中文（简体） · 3 es Español · 4 ar العربية · 5 pt Português · 6 id Bahasa Indonesia · 7 fr Français ·
8 ja 日本語 · 9 ru Русский · 10 de Deutsch · 11 hi हिन्दी · 12 ko 한국어 · 13 tr Türkçe · 14 it Italiano · 15 vi Tiếng Việt ·
16 pl Polski · 17 nl Nederlands · 18 th ไทย · 19 uk Українська · 20 ro Română · 21 el Ελληνικά · 22 cs Čeština · 23 sv Svenska ·
24 hu Magyar · 25 fa فارسی · 26 pt-BR Português (Brasil) · 27 zh-TW 中文（繁體） · 28 sq Shqip · 29 am አማርኛ · 30 be Беларуская ·
31 bn বাংলা · 32 bs Bosanski · 33 bg Български · 34 ca Català · 35 hr Hrvatski · 36 da Dansk · 37 et Eesti · 38 tl Filipino ·
39 fi Suomi · 40 gu ગુજરાતી · 41 ha Hausa · 42 he עברית · 43 ga Gaeilge · 44 jv ꦧꦱꦗꦮ · 45 kn ಕನ್ನಡ · 46 lv Latviešu ·
47 lt Lietuvių · 48 mk Македонски · 49 ms Bahasa Melayu · 50 ml മലയാളം · 51 mt Malti · 52 my မြန်မာ · 53 no Norsk ·
54 pa ਪੰਜਾਬੀ · 55 sr Српски · 56 sk Slovenčina · 57 sl Slovenščina · 58 sw Kiswahili · 59 ta தமிழ் · 60 te తెలుగు ·
61 ur اردو · 62 yo Yorùbá

The canonical list lives in `src/PurgeCad.Localization/Languages.cs`. Strings are JSON files, one per language
(`src/PurgeCad.Localization/Strings/<code>.json`); English is the source and fallback. See `docs/LOCALIZATION.md`.

## 9. Architecture

```
src/
  PurgeCad.Core                 no UI, no CAD library
    Scanning/                   folder scan, counts, auxiliary file discovery
    Purging/                    IPurgeEngine, options, results, engine registry, risk info
    Batch/                      batch runner: modes, parallelism, progress, cancel, atomic write
    Cleanup/                    auxiliary file cleaner, lock check, recycle bin per OS
    Reporting/                  CSV / text report
    Settings/                   user settings (JSON in the OS config folder)
    Branding/                   names, URLs, attribution text (single source)
  PurgeCad.Engines.ACadSharp    IPurgeEngine on ACadSharp
  PurgeCad.Engines.AutoCad      IPurgeEngine that drives accoreconsole (Windows)
  PurgeCad.AutoCad.Plugin       DLL loaded inside accoreconsole (net462 + net8.0-windows), AutoCAD API from NuGet (compile-time only)
  PurgeCad.Localization         language list, JSON string loader, fallback to English
  PurgeCad.App                  Avalonia UI: Themes (tokens), Views, ViewModels, Controls
tests/
  PurgeCad.Core.Tests
  samples/                      test drawings (generated, no user files)
```

**Extension points:**
- New engine → implement `IPurgeEngine`, register it in `EngineRegistry`.
- New batch operation (e.g. convert version, audit) → implement `IBatchOperation`; the runner, progress and report are reused.
- New language → add a JSON file; the list in `Languages.cs` is fixed to the 62 above.
- New look → change tokens only.

## 10. Branding and attribution

Single source: `src/PurgeCad.Core/Branding/Brand.cs`.
- About window: "PurgeCAD by Syntelab · part of Syntelab Apps", links to syntelab.com, syntelab.com/apps, the app page (localized URL), source code, license, third-party notices.
- Forks must keep the attribution notice (GPLv3 §7(b)) and may not use the names "PurgeCAD", "Syntelab" or the logo for their own product (§7(e)). See `ADDITIONAL-TERMS.md` and `TRADEMARKS.md`.

## 11. Testing plan (before any release)

1. **ACadSharp proof:** purge 20–30 real drawings (copies), open every result in AutoCAD, run AUDIT, compare visually. Go/no-go for the ACadSharp engine.
2. Sample folder: DWG of every version (R14…2018), DXF ASCII/binary, read-only, locked, 0 bytes, corrupt, too new, with proxy objects, with xrefs, nested subfolders, `.dwl` with and without an open drawing.
3. Unit tests for scanning, auxiliary cleanup rules, report, settings, localization (every JSON has every key; RTL flag).
4. UI check in all three OSes, light and dark, with en, ar (RTL), ja (CJK), hi, my, jv (complex scripts), de/fi (long strings).

**No builds are run until the first test session is agreed.**

## 12. Open questions
- Exact wording of the additional attribution terms (draft in `ADDITIONAL-TERMS.md`).
- Who translates the 61 non-English languages, and review process.
- Code signing for Windows / macOS (notarization) — needed to avoid "unknown developer" warnings.
- GitHub organisation / repository name.
