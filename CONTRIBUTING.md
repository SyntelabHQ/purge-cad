# Contributing to PurgeCAD

Thank you for helping. By contributing, you agree that your contribution is licensed under the
GNU GPL v3 with the additional terms in `ADDITIONAL-TERMS.md`, like the rest of the project. No CLA is required.

## Ground rules

- **Code, comments, commit messages and documentation are in English.** Interface text goes in the language JSON files.
- **No hard-coded colours, sizes or fonts in views.** Use tokens from `src/PurgeCad.App/Themes/Tokens.axaml`.
- **No user-visible text in code.** Use a localization key; add it to `Strings/en.json`.
- **Never risk user files.** Every write goes to a temp file first, then replaces the original.
- Keep modules independent: `PurgeCad.Core` must not reference Avalonia or any CAD library.

## Adding things

- A purge engine: implement `IPurgeEngine` (`src/PurgeCad.Core/Purging`) in a new project, register it in `EngineRegistry`.
- A batch operation: implement `IBatchOperation` (`src/PurgeCad.Core/Batch`).
- A translation: see `docs/LOCALIZATION.md`.

## Test drawings

Never commit drawings you do not own. Test drawings in `tests/samples` are generated or explicitly licensed.
