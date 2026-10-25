# ACadSharp proof run

Runs the ACadSharp engine on a folder of drawings and writes purged copies plus a report. Originals are only read.

```powershell
cd <repo>
dotnet run -c Release --project tools\PurgeCad.Proof -- tests\samples tests\output *> proof.log
```

Output: `tests/output/<same relative paths>`, `tests/output/report.md`, `tests/output/report.csv`.
Test drawings in `tests/samples` are private and ignored by git.
