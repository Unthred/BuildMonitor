# Bug fix prompt

```text
Bug: <symptom>

Repro:
  <steps or log excerpt>

Instruction:
  Find root cause, fix with minimal diff.
  Add regression test in BuildMonitor.Tests if logic in Core/Infrastructure changed.
  Update docs if behaviour doc is wrong.
  User runs dotnet build and dotnet test — agent does not run them unless explicitly asked.
```
