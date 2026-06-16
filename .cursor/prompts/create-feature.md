# Create feature prompt

Create a production-ready feature for **BuildMonitor** using existing repo layout and conventions.

```text
Feature: <short name>

Goal:
  <what the user gains>

Surfaces:
  <TrayApp / Orchestrator / Settings / docs>

Instruction:
  1. Run feature-kickoff flow — issue # or create one + **mandatory** `gh project item-add 3`; branch feature/<id>-kebab
  2. Implement with minimal diff; Core/Infrastructure logic gets BuildMonitor.Tests coverage
  3. Update docs/features/<name>.md and docs/README.md in same change
  4. User runs dotnet build / dotnet test — agent supplies commands only
  5. Ship with feature-ship when user says ship it
```
