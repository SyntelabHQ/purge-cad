# Releasing a new version

PurgeCAD updates itself from GitHub releases with [Velopack](https://velopack.io) (open source, MIT):
users who installed it with **PurgeCAD-win-Setup.exe** see "Version x available" in the title bar and update with
one click (download, close, install, restart; settings in %APPDATA% are kept). A copy that was not installed with
the setup (a build folder) only gets a link to the release page.

## Download links (never change between versions)

| | Link |
|---|---|
| Windows installer | `https://github.com/SyntelabHQ/purge-cad/releases/latest/download/PurgeCAD-win-Setup.exe` |
| Windows portable | `https://github.com/SyntelabHQ/purge-cad/releases/latest/download/PurgeCAD-win-Portable.zip` |
| macOS, Linux | added to the same release by GitHub Actions (see below); names shown on the release page |

The installer installs per user (no administrator rights) in `%LocalAppData%\PurgeCAD`.

## Steps

1. Raise `<Version>` in `Directory.Build.props` (for example `1.1.0`, see "Version numbers" below).
2. On Windows, in the repository folder:
   ```powershell
   $env:GITHUB_TOKEN = "<token with contents: write on SyntelabHQ/purge-cad>"
   .\build\release-windows.ps1 -Upload -Notes docs\notes\1.1.0.md
   ```
   It runs the tests, builds, packs and creates the GitHub release `v1.1.0` with the Windows files.
   Without `-Upload` it only builds, into `artifacts\releases` (try the installer first).
   A versioned copy of the installer stays in `artifacts\PurgeCAD-1.1.0-win-Setup.exe`.
3. Publishing the release starts the workflow `.github/workflows/release.yml`, which builds macOS (Apple silicon
   and Intel) and Linux and adds their files to the same release. It can also be run by hand for a tag.

## Code signing

Unsigned, Windows SmartScreen warns on the first download ("Windows protected your PC"). With a code-signing
certificate, pass the signtool parameters: `-SignParams '/a /fd sha256 /tr http://timestamp.digicert.com /td sha256'`.
macOS builds are unsigned too; macOS asks the user to allow the app (right-click › Open) until they are signed
and notarized with an Apple Developer ID.

## Version numbers

MAJOR.MINOR.PATCH: PATCH for fixes only (1.0.0 → 1.0.1), MINOR for new features (1.0.1 → 1.1.0), MAJOR for
changes that break how things worked (1.4.2 → 2.0.0). Drafts and pre-releases are never offered as updates.

A fork that publishes its own releases sets `Brand.UpdateRepository` and the `-Repository` parameter to its repository.
