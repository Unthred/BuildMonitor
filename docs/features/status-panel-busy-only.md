# Status panel: busy projects only when it opens by itself

Issue [#173](https://github.com/Unthred/BuildMonitor/issues/173). With many derived
worktrees registered, a pop-up for one build used to list a full card for every
active project and covered most of the screen.

## Behaviour

| Step | What | Where |
|------|------|-------|
| 1 | Panel opens by itself (Local or Azure build activity hold, edit-gating countdown, busy work): switch to **busy projects** | `App.SyncBuildActivityStatusPanelVisibility`, `App.AutoShowStatusPanelForEditGating` → `ShowStatusPanel(StatusPanelScope.BusyProjects)` |
| 2 | Tray icon hover, tray click, or tray menu **Show status**: switch to **every project**. Hovering the icon while a pop-up is open expands it | `App.OnNotifyIconMouseMove`, `App.ToggleStatusPanel`, `TrayMenuHost.ShowStatus` |
| 3 | Busy = building, testing, waiting for edits, edit gating, restarting, a failed build/test/run, or Azure CI activity when *keep visible during Azure CI activity* is on | `StatusPanelFocus.IsBusy` |
| 4 | A project busy at any point while the panel is up keeps its card until the panel hides, so a finished build still shows its result and site link | `StatusPanelFocus.Apply` |
| 5 | Header reads **Build status (N of M)** with a tooltip pointing to tray hover for all projects | `StatusPanelFocus.FormatHeader`, `HoverStatusPanel.Update` |
| 6 | Panel hides: scope resets to every project, so the next pop-up starts a fresh list | `HoverStatusPanel` `IsVisibleChanged` |

The side rail (Overall), header countdown and *Still editing* button still use every
active project; only the cards are filtered.

**Failure and fallback:** if the panel is in busy mode but no project has been busy
yet, every card is shown rather than an empty panel. A project whose last build
failed counts as busy, so it stays in the pop-up until it is fixed or the panel is
opened by hover instead.
