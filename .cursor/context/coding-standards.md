# Coding standards

C# style for this solution. Layout: `.cursor/rules/architecture.mdc`.

- Use file-scoped namespaces.
- Use nullable reference types.
- Prefer explicit types when clarity improves.
- Keep methods short; keep files short — see `.cursor/rules/code-structure.mdc` (≤400 lines target, no god-class growth).
- Avoid magic strings for settings keys — use `LocalAppSettings` properties.
- Test pure logic in `BuildMonitor.Tests`; orchestration changes need Tier 2 tests — see `.cursor/rules/testing.mdc`.
