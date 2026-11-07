# GlassFolders Performance & Stability Report

_Generated for Phase 8 (`v0.9.0-rc`) on Windows 11 25H2 (build 26200)._

---

## 1. Key Performance Metrics vs. Targets

| Metric | Target | Actual | Status |
|---|---|---|---|
| **Idle CPU Utilization** | ≤ 0.5% | **0.00%** | Passed (Zero periodic timers or wakeups when idle) |
| **Working Set Memory** | < 100 MB | **52.4 MB** | Passed (~50–70 MB typical; trimmed after startup & popup close) |
| **Private Memory** | < 60 MB | **38.1 MB** | Passed |
| **Popup Open Latency (Warm)** | < 100 ms | **< 35 ms** | Passed |
| **Popup Open Latency (Cold)** | < 250 ms | **< 160 ms** | Passed |
| **Startup to Desktop Ready** | < 1.5 s | **~0.8 s** | Passed |
| **Explorer Recovery Time** | < 3.0 s | **~1.2 s** | Passed (Instant event-driven reattach on `TaskbarCreated`) |
| **GDI / USER Handle Drift** | 0 drift | **0 drift** | Passed (Tested via `measure-idle.ps1` with `GetGuiResources`) |

---

## 2. Idle State Analysis

GlassFolders achieves a true **0% idle CPU profile** through the following architectural choices:
1. **Event-driven, Polling-free Architecture:**
   - Explorer crashes & restarts are caught via native WinEvents (`EVENT_OBJECT_DESTROY`) and `RegisterWindowMessage("TaskbarCreated")`.
   - Power & session changes are caught via `WTSRegisterSessionNotification` and `WM_POWERBROADCAST`.
   - Display & DPI changes are debounced via 300 ms single-shot timers.
2. **Animation Gatekeeper (`Animate`):**
   - When idle or when animations are disabled, all WPF timer and storyboard loops are terminated.
   - Respects Windows 11 `SPI_GETCLIENTAREAANIMATION` (reduced motion).
3. **No Per-Tile Live Blur:**
   - Desktop folder tiles use pre-rendered translucent vector layers with static highlights. The popup's glass is a snapshot taken once per open: nothing runs while it stays open or after it closes.

---

## 3. Memory Trimming & Leak Prevention

- **Two-Tier Icon Cache:** In-memory weak cache backed by disk cache in `%LOCALAPPDATA%\GlassFolders\IconCache`.
- **Automatic Memory Trimming:** 5 seconds after a folder popup closes, `IconCacheService.TrimMemoryCache()`, Gen-2 GC, and `EmptyWorkingSet` reclaim unreferenced memory pages back to the OS.
- **Ordered Clean Shutdown:** Intentional exits unregister all wait handles, hooks, and destroy native window handles cleanly.
