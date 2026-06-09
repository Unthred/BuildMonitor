# Performance guidelines

Outcomes BuildMonitor optimizes for:

- Responsive tray UI (no blocking on subprocess I/O)
- Fast time-to-listening-url after build
- Bounded log memory and disk writes
- Efficient watch rebuild coalescing

Concrete rules: `.cursor/rules/performance.mdc` and `DotNetProcessConfigurator` / `ProjectOrchestrator` debouncing.
