# Code Review: boot guard lifecycle wiring (uncommitted diff vs 4f40c32)

Scope: EnforcementController, PolicyReconciler, ScheduledTaskDeadManSwitch, StartupReconcileTask, App (App.xaml.cs, CoreServices, FixedInstall, EnforcementViewModel), tools/remove-boot-guard.ps1, tests.
Tests: `dotnet test --filter BootGuard|Reconcile|DeadMan`: 68/68 pass. They pass because the fakes only throw the exception types the code already catches.

## Critical

### C1. The boot reconcile can exit without releasing the guard, leaving the machine offline
- `src/EVBlocker.Core/Startup/PolicyReconciler.cs:106-117, 196-281`
- `Run()` only reaches `SettleBootGuard` if `ReconcileRules()` returns normally. `ReconcileRules` catches only `UnauthorizedAccessException`, `InvalidOperationException`, `InvalidDataException`, `IOException`. Some exceptions it does not catch:
  - `COMException` with any HRESULT other than E_ACCESSDENIED. `WindowsFirewallPolicy.Guard` passes these through unchanged (`WindowsFirewallPolicy.cs:190-203`). This can happen from `GetStatus`/`Plan`/`Apply`, e.g. MpsSvc answers the default-action read but a rule enumeration or add fails while it is still settling, or a rule is rejected with E_INVALIDARG.
  - `TimeoutException` from `ProcessRunner` (`ProcessRunner.cs:77`), via `GetStatus` -> `_deadMan.IsArmed()` -> `schtasks /Query` with a 30 s timeout. A loaded boot makes this plausible.
  - `Win32Exception` if schtasks.exe cannot be started, and `ArgumentException` from `BuildDesiredRules` on odd data.
- What happens: the exception leaves `Run`. `App.RunReconcile` catches it, logs "Unhandled" and returns 1. The guard stays **Engaged**, so there is no outbound traffic until someone runs the recovery by hand. The documented contract ("any failure -> Release anyway") is broken.
- Fix: settle the guard no matter what the rules step does. Either wrap `ReconcileRules()` in `try { ... } catch (Exception ex) { report = Failed(ex) }` (CA1031 is justified here), or put `SettleBootGuard` in a `finally`. Add tests where `FakeFirewallPolicy` throws `COMException` / `TimeoutException` from `GetStatus` and from `Apply`, and assert `Guard:Release`.

### C2. `FixedInstall.Ensure` copies only the apphost; a non-single-file build installs a copy that cannot start
- `src/EVBlocker.App/Services/FixedInstall.cs:43-51`
- Only `Environment.ProcessPath` is copied. For every non-published build (`bin/Debug/net8.0-windows/`, `bin/Release/...`, `dotnet run`) that file is an apphost that needs `EVBlocker.dll`, `EVBlocker.Core.dll`, deps.json and runtimeconfig.json next to it. The listing of `bin/Debug/net8.0-windows` confirms this. Only the `SelfContained.pubxml` single-file publish is a single executable.
- What happens: a developer or tester enables blocking from a build folder. `Program Files\EVBlocker\EVBlocker.exe` cannot start, the startup task fails at every boot, and after a reboot the guard stays Engaged with no network. The dead-man's `--remove-boot-guard` action also fails, though its netsh import still runs.
- Fix: in `Ensure`, refuse to continue unless the process is single-file. For example, throw `InvalidOperationException` when `!string.IsNullOrEmpty(typeof(FixedInstall).Assembly.Location)`, which is empty only in single-file mode. That aborts Enable before `InstallBootGuard`, which is safe (test `Enable_PreparationFails_NoGuardAndNothingBlocked`). Or copy the whole `AppContext.BaseDirectory`. Do not only document it.

## High

### H1. `InstallBootGuard` skips the preparation when the guard is already Released, so a missing startup task is never re-created
- `src/EVBlocker.Core/Firewall/EnforcementController.cs:290-298`
- The early return on `GetState() == Released` also skips `_prepareBootRelease`. There are two ways to reach this state:
  1. `Disable` sets Allow, then `_bootGuard.Remove()` throws (line 244). The guard stays Released and State is Off. Then either:
     - Enable runs again: `InstallBootGuard(prepare:true)` returns early, so no startup task check is made, and Block is applied.
     - Or the user removes the startup task (allowed, see H2) and later enables.
  2. The startup task is deleted outside the app (Task Scheduler UI, `schtasks /delete`, cleanup tools) while blocking is on. The VM constructor's `SyncBootGuard(prepare:true)` is documented as the catch-up path, but it returns early because the guard is Released and it never re-registers the task. The VM's separate `FixedInstall.Ensure()` runs only when `_startupInstalled`.
- What happens: blocking is on, the guard is Released now and Engaged after reboot, and nothing releases it. The machine is offline.
- Fix: when `prepare` is true, always run `_prepareBootRelease` (it is idempotent: hash compare plus `/F` register), and only skip `Install()` when the guard is Released. Add a test: `Sync_AlreadyReleased_StillPrepares` / `Enable_WithLeftoverReleasedGuard_Prepares`. The existing test `Sync_AlreadyReleased_DoesNothing` locks in the buggy behaviour.

### H2. The startup task can be removed while a guard still exists
- `src/EVBlocker.App/ViewModels/EnforcementViewModel.cs:75-77, 305-311`
- The canExecute condition is `!(_startupInstalled && State != Off)`. `State` comes from the firewall default action, not from the guard. After a failed guard removal in Disable, a `netsh advfirewall reset`, a GPO flip to Allow, or a dead-man revert whose `--remove-boot-guard` step failed, State is Off but the guard is still present. The toggle is then enabled and `Uninstall()` runs.
- What happens: the next boot has the guard Engaged and no task to release it, so the machine is offline.
- Fix: in `ToggleStartupTask`'s uninstall branch, call `_controller.SyncBootGuard(prepare:false)` first (this removes the guard when not blocking). Then refuse to uninstall unless the guard state is Absent. Alternatively, expose the guard state to the canExecute condition.

## Medium

### M1. `WaitForFirewall` measures elapsed time as the sum of sleeps, not wall-clock time
- `src/EVBlocker.Core/Startup/PolicyReconciler.cs:128`
- `waited += PollInterval` does not count the time spent inside `GetDefaultOutboundActions()`. If each failing call blocks (RPC to a service that is starting can take many seconds), 180 iterations can run longer than the task's `PT10M` `ExecutionTimeLimit`. Task Scheduler then kills the process, and the guard is never released (same outcome as C1).
- Fix: use `Stopwatch`/`Environment.TickCount64` for the deadline. The test seam can inject a clock, or keep `_sleep` and add a `Func<TimeSpan> elapsed`.

### M2. The release filter is lost if BFE restarts without a reboot
- The release permit is non-persistent. If BFE/MpsSvc restarts (service crash, some servicing operations, an admin restarting BFE), non-persistent objects are dropped and the persistent block filters engage in the middle of a session. Only a reboot (startup task) or opening the app elevated re-releases the guard.
- Fix: add a second trigger to the startup task, an EventTrigger on BFE/MpsSvc service start (System log, Service Control Manager 7036, or the Windows Firewall operational log). Or at least document that recovery is to reboot or open the app.

### M3. New call sites for exceptions the ViewModel's `Run` does not catch (UI crash)
- `EnforcementViewModel.cs:106` (constructor `SyncBootGuard(prepare:true)`), and Enable via `PrepareBootRelease`.
- The new paths can throw:
  - `TimeoutException` / `Win32Exception` from `SchTasksHost` (`ProcessRunner`)
  - `IOException` (not `InvalidDataException`) from `File.WriteAllText` of the temp XML in `Register`
  - `FileNotFoundException` from `StartupReconcileTask.Install` if `Ensure` returned but the file vanished
  - non-access-denied `COMException` from `GetDefaultOutboundActions` inside `SyncBootGuard`.
- Any of these reaches `OnDispatcherUnhandledException`, and the constructor case means the main window never opens. The pattern existed before, but these calls now run on every elevated launch.
- Fix: in `Run`, also catch `IOException`, `TimeoutException`, `System.ComponentModel.Win32Exception`, and `COMException`, or wrap them where they are thrown (`FixedInstall`/`SchTasksHost` into `InvalidOperationException`).

### M4. `FixedInstall.Ensure` hashes the whole executable on the UI thread at every elevated launch
- `FixedInstall.cs:44, 73-78`, called from the VM constructor. On an elevated start it can run twice: once explicitly, and again through `SyncBootGuard` -> prepare when the guard is not Released.
- The single-file exe is about 63 MB and the fixed copy the same, so two full SHA-256 reads block window creation.
- Fix: compare length plus `LastWriteTimeUtc` plus `FileVersionInfo` first, and hash only on a tie. Or move the work off the UI thread.

## Low

- **L1. Unconditional guard removal in the dead-man task** (`ScheduledTaskDeadManSwitch.cs` BuildTaskXml). `--remove-boot-guard` runs whether or not the netsh import succeeded. The task has StartWhenAvailable, so it can fire at boot before MpsSvc is ready. If that import fails, blocking stays on and the guard is gone, so later boots leak until an elevated app launch runs SyncBootGuard. This fails open, which is acceptable. Better: make the second action `--reconcile`-style (sync against the actual default action) rather than blind removal.
- **L2. Concurrent guard writes at boot.** The dead-man (StartWhenAvailable at boot) and the startup reconcile can run at the same time. The reconcile reads Block, then the dead-man imports Allow and removes the guard, then the reconcile re-installs it. The next boot's reconcile removes it again. This heals itself, but it only works because of C1/H1 being right. No action beyond a comment.
- **L3. The fixed copy can be downgraded.** `Ensure` overwrites the fixed copy with whatever version was launched elevated, including an older one. Consider comparing `FileVersionInfo` and not downgrading.
- **L4. Leftover staging file.** When the Move fails (fixed copy running), `EVBlocker.exe.new` is left in Program Files. It is harmless, but should be cleaned up in `catch`.
- **L5. MessageBox under SYSTEM** (`App.xaml.cs:192,210`). The `IsSystem` check is correct for the dead-man principal (S-1-5-18). The `--reconcile` and `--remove-boot-guard` paths never reach `OnDispatcherUnhandledException`'s MessageBox except for failures during App construction/XAML load. That is pre-existing but more consequential now that SYSTEM runs this binary at every boot.

## Test gaps
1. No reconcile test with exception types outside the caught set (C1). This is the most important gap.
2. `Sync_AlreadyReleased_DoesNothing` asserts the H1 bug. Add a test for Released guard plus missing startup task.
3. No test for WaitForFirewall wall-clock behaviour when each poll is slow (M1).
4. No tests for FixedInstall (single-file detection, same-path skip, running-target failure mapping). The App project has no test project; consider moving the pure logic into Core.
5. No VM-level test for the startup-task toggle while the guard is present but State is Off (H2).

## Verified OK (not re-litigated)
- Enable order (Arm -> prepare -> Install released -> Block) is correct. If it fails before Block, nothing is blocked, and the revert removes a guard left installed.
- Disable order (Allow -> Remove) fails open.
- The dead-man XML puts the remover after netsh, escapes paths, and is omitted when no remover is set.
- Startup task XML: no delay, Priority 4 (normal band), PT10M > 3 min wait + 2 min (tested).
- The recovery script keys are checked against BootGuardFilters by a test.

Status: DONE_WITH_CONCERNS
Summary: Two ways the machine can be left offline after a reboot: exceptions outside the caught set skip the guard release in PolicyReconciler (C1), and FixedInstall copies only the apphost, which cannot start in non-single-file builds (C2). A Released guard also skips re-registering the startup task (H1), and the startup task can be removed while a guard remains (H2).
Concerns/Blockers: Fix C1, C2, H1 and H2 before shipping. M1 is the same failure as C1 through a timeout.
