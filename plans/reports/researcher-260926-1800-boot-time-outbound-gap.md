# Boot-time outbound gap in Windows Firewall (WFP/BFE/MpsSvc) — research report

Scope: does a real window exist at Windows startup where a to-be-blocked app can send outbound
traffic before EVBlocker's policy is enforced. Report-only, no code changed.

## TL;DR

The doc line at `docs/huong-dan-su-dung.md:304` is **directionally right but overstated/imprecise**.
A boot-time phase with different rules than steady-state does exist and is a genuine Windows
platform limit EVBlocker's rule-based approach cannot close. But that phase is **default-deny by
Windows itself**, not default-allow, and it ends before most third-party apps could realistically
be running. The bigger, more concrete gap for this project is **not boot timing** — it's **policy
drift** (something external resets `DefaultOutboundAction` or deletes rules), which
`PolicyReconciler`/`StartupReconcileTask` already partially, deliberately, does not auto-heal for
the default action (see `src/EVBlocker.Core/Startup/PolicyReconciler.cs:46-58`).

## 1. What Windows enforces, phase by phase

Windows Filtering Platform (WFP) has two filter classes relevant here, both documented by
Microsoft: **boot-time filters** (`FWPM_FILTER_FLAG_BOOTTIME`) and **persistent filters**
(`FWPM_FILTER_FLAG_PERSISTENT`) — mutually exclusive flags.
[MS Learn: WFP Operation](https://learn.microsoft.com/en-us/windows/win32/fwp/basic-operation),
[MS Learn: FWPM_FILTER0](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_filter0)

- **tcpip.sys init → BFE start ("boot-time" phase):** "At boot-time, as soon as the TCP/IP stack
  driver (tcpip.sys) starts, the kernel-mode filter engine enforces the security policy of the
  system through boot-time filters." These are removed the instant BFE starts.
  [MS Learn: WFP Operation](https://learn.microsoft.com/en-us/windows/win32/fwp/basic-operation)
  Microsoft's own filter-origin doc names this phase's default explicitly: **"Boot time default"**
  block filter — "occur[s] when the computer is booting up and the firewall service isn't yet
  running. Services need to create a boot time allow filter to allow the traffic. **It should be
  noted that it's not possible to add boot time filters through firewall rules.**"
  [MS Learn: Filter Origin Audit Log](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/filter-origin-documentation)
  → **This phase is default-block by Windows itself**, not open. It is not EVBlocker's
  `DefaultOutboundAction`; it is a distinct, separate, Windows-native mechanism, and Windows
  Firewall *rules* (what EVBlocker's COM API writes) cannot participate in it at all.

- **BFE start → policy fully "settled":** "Once the Base Filtering Engine (BFE) starts in user
  mode, persistent filters are added to the platform, boot-time filters are disabled... The
  transition from boot-time to persistent filters could be several seconds, or even longer on a
  slow machine. **It is atomic**, so if a provider has both a boot-time and a persistent filter,
  there will never be a window when neither is in effect."
  [MS Learn: WFP Operation](https://learn.microsoft.com/en-us/windows/win32/fwp/basic-operation)
  Persistent filters are stored by BFE itself (registry-backed: `HKLM\SYSTEM\ControlSet001\
  Services\BFE\Parameters\Policy\Persistent`) and reloaded by BFE at its own startup via
  `BfePersistentPolicyInit`, independent of whether the MpsSvc *process* has finished its own
  startup logic yet — this is corroborated by reverse-engineering (Quarkslab) and by the widely
  observed fact that **stopping the `MpsSvc` service does not turn off enforcement**, because BFE
  keeps enforcing whatever was last pushed to it.
  [Quarkslab: WFP persistent state under the hood](https://blog.quarkslab.com/windows-filtering-platform-persistent-state-under-the-hood.html)
  [MS Q&A: Is Windows Defender Firewall Service always necessary](https://learn.microsoft.com/en-us/answers/questions/2111512/is-the-windows-defender-firewall-service-always-ne)
  → Practical implication: if EVBlocker's `DefaultOutboundAction=Block` + allow-list rules were
  already applied (i.e. written at least once via `MpsSvc`/the COM API on a prior run), Windows
  Firewall's own rule-derived WFP filters are marked persistent and get **reloaded by BFE the
  moment BFE starts** — this is *before* `MpsSvc` itself is confirmed "done." So the repo's own
  comment in `src/EVBlocker.Core/Startup/StartupReconcileTask.cs:9-14` ("blocking... is applied by
  MpsSvc, which starts long before any user-mode program; the policy is already in force by the
  time anything here could run") is **correct in effect**, though the precise mechanism is "BFE
  reloads MpsSvc's previously-persisted filters," not "MpsSvc re-applies live" — this distinction
  is inferred from the sources above and from `basic-operation.md`'s wording, **not from a single
  explicit MS statement** that Windows Firewall's own rule filters carry the PERSISTENT flag. Flag
ged as high-confidence inference, not a directly-cited fact.

- **Storage:** Rules are not "re-added by MpsSvc at start" in the sense of MpsSvc recomputing them
  from GPO/local policy every boot before they take effect — they're already sitting in BFE's own
  persistent store and load with BFE. `MpsSvc` reconciles/refreshes on top (GPO changes, service
  restarts) but the last-known-good filter set is enforced without waiting for it.

## 2. Can user-mode processes even run and use the network in the gap? Is it real?

- `tcpip.sys` is a **boot-start** driver, loaded by the OS loader as part of the boot volume's
  driver stack — on Windows 11 even before full kernel init. `BFE` is an **Auto-start** *service*
  in the `NetworkProvider` load-order group with a dependency on `RpcSs`, started later by the
  Service Control Manager (SCM) once `services.exe` is running.
  [revertservice: tcpip driver defaults](https://revertservice.com/11/tcpip/)
  [MS Learn archive: Boot order of Windows Services](https://learn.microsoft.com/en-us/archive/msdn-technet-forums/445ddb5e-16b4-4dca-ab89-c0d0ec728af5)
  → There genuinely is a temporal window between "network stack is up" and "BFE is up," and SCM
  starts Auto-start services in parallel batches by dependency graph, not by a single serialized
  order — so a third-party service with no explicit dependency on BFE/MpsSvc *could* start in that
  window in principle.
- But regular desktop **applications** (as opposed to Windows *services*) do not run this early —
  they start at user logon at the earliest, by which point BFE (and normally MpsSvc) have already
  been running for a while, since they're core dependencies many other Automatic services need.
  Combined with fact 1 above (Windows' own "Boot time default" filter blocks by default in this
  window), a third-party **service** racing to start before BFE would still be **blocked by
  Windows' own boot-time default**, not "open," unless it had a boot-time *allow* filter of its own
  pre-registered — which requires the low-level WFP API and (per the MS doc above) cannot be done
  through firewall rules at all, so it's out of reach for ordinary commodity software.
- Net: the gap is **real as a kernel/driver-timing phenomenon**, but **not "wide open"** — Windows'
  own default during that phase is deny, and essentially no ordinary user-mode app is a candidate
  to exploit it. This matches the "hole" project's investigation of the same question for a
  different Windows kill-switch app (community source, not Microsoft, but a serious, on-point
  technical writeup):
  [GitHub bindreams/hole#998](https://github.com/bindreams/hole/issues/998) — they treat the gap as
  real for a VPN kill switch built on raw WFP (which *can* add boot-time filters) currently only
  using `PERSISTENT`; not directly analogous to EVBlocker, which only has the firewall-rules API.

## 3. Concrete way to close it — and why it doesn't fit this project

| Approach | What it does | Limits | Risk |
|---|---|---|---|
| Add `FWPM_FILTER_FLAG_BOOTTIME` block-all + allow loopback/DHCP/DNS via raw WFP (`FwpmFilterAdd0`) | Extends default-block to the exact tcpip.sys→BFE window with an explicit exception list | Per MS docs, **firewall rules (COM API/netsh/PowerShell) cannot create boot-time filters at all** — must call the native `fwpuclnt` WFP management API directly, as its own privileged component, typically re-registered every boot | Misconfigured boot-time block-all can hard-lock the machine off the network before any UI/recovery path exists (no MpsSvc, no logon network yet); needs a tested out-of-band removal path (e.g. WinRE, safe mode, or a "delete boot-time filters" recovery script) before ever shipping |
| Rely on `FWPM_FILTER_FLAG_PERSISTENT` only (current approach via Windows Firewall rules) | Covers everything from BFE start onward, which per §1 is effectively "as soon as BFE loads its persisted store" | Does **not** cover the pre-BFE boot-time phase at all (by design/flag exclusivity with BOOTTIME) | None beyond what already exists — this is what EVBlocker already gets for free from the COM API |
| Do nothing extra (status quo) | Boot-time phase is covered by Windows' own default-block; persistent phase is covered by already-applied rules | Leaves the literal tcpip.sys→BFE micro-window unmanaged by EVBlocker specifically | Low: per §1/§2, that window is already default-deny and not realistically reachable by ordinary third-party apps |

**Recommendation: do nothing here.** Given (a) COM/`INetFwPolicy2` cannot produce boot-time
filters at all — this is an explicit platform restriction, not a missing feature to build around —
and (b) the phase is already default-deny with no plausible ordinary-app exploit path, building a
raw-WFP boot-time-filter feature would trade a already-mitigated, currently-non-actionable risk for
a **real, immediate risk**: a bricked/lockout machine from a boot-time block-all with any bug, plus
a large new engineering surface (raw WFP session management, its own persistence/versioning,
recovery tooling) that YAGNI/KISS argue against for a "block internet for a specific commodity app"
tool. This would only be worth revisiting if the threat model changes to include privileged/rootkit
adversaries — but at that trust level the adversary can also just delete EVBlocker's rules directly
via the same privileges, so boot-time filters wouldn't meaningfully raise the bar for *this*
project's stated threat model.

## 4. Is the docs claim (line ~304) accurate?

> "Có một khe hở rất sớm trong quá trình khởi động, trước khi MpsSvc áp đầy đủ chính sách. Đây là
> đặc tính của Windows, không kiểm soát được."

- **Accurate part:** there is a Windows-native early-boot phase (tcpip.sys→BFE) governed by a
  different mechanism than ordinary firewall rules, and EVBlocker's rule-based approach
  structurally cannot touch it (confirmed by MS doc: firewall rules can't create boot-time
  filters). That part of "đặc tính của Windows, không kiểm soát được" is correct.
- **Overstated/imprecise part:** it implies an open window before the block applies. In reality
  (i) that early phase is itself **default-block** by Windows, not open, and (ii) once BFE starts,
  persistence means EVBlocker's own policy (if previously applied) is already active — enforcement
  does not actually wait for "MpsSvc áp đầy đủ chính sách" to finish; it rides in with BFE's own
  reload of the persisted filter set. So the practical risk window is much smaller than the
  sentence implies, and framing it as "before MpsSvc applies policy" misattributes the mechanism.
- **What's missing from the doc:** the actually-relevant, project-specific gap isn't boot timing —
  it's that `PolicyReconciler` intentionally never restores `DefaultOutboundAction` on its own
  (`src/EVBlocker.Core/Startup/PolicyReconciler.cs:50-58`, deliberate, to avoid guessing user
  intent), so if anything external ever resets it to Allow, the machine stays open until a human
  re-arms it — no boot-timing story needed to explain that gap, and it's a much larger, more
  actionable, and more likely real-world exposure than the WFP micro-window.

**Suggested rewrite direction (not applied, doc not edited per scope):** replace the boot-timing
framing with something like: "Windows tự chặn mặc định ở giai đoạn sớm nhất lúc khởi động (trước
cả khi dịch vụ tường lửa chạy) — không phải một khe hở mở. Nhưng nếu có gì đó bên ngoài đưa
`DefaultOutboundAction` về Allow (reset tường lửa, Windows Update, GPO khác), EVBlocker sẽ không tự
bật lại — máy sẽ mở cho tới khi bạn kiểm tra lại thủ công." — this is both more accurate and more
actionable for the reader.

## Sources

- [MS Learn — WFP Operation (boot-time vs persistent filters, atomicity)](https://learn.microsoft.com/en-us/windows/win32/fwp/basic-operation)
- [MS Learn — About Windows Filtering Platform ("boot-time security until BFE can start")](https://learn.microsoft.com/en-us/windows/win32/fwp/about-windows-filtering-platform)
- [MS Learn — Filter Origin Audit Log ("Boot time default" filter; "not possible to add boot time filters through firewall rules")](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/filter-origin-documentation)
- [MS Learn — FWPM_FILTER0 (BOOTTIME/PERSISTENT flags mutually exclusive)](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_filter0)
- [MS Learn — Filtering conditions available at each filtering layer](https://learn.microsoft.com/en-us/windows/win32/fwp/filtering-conditions-available-at-each-filtering-layer)
- [MS Learn — WFP Layer Requirements and Restrictions (no explicit boot-time condition restriction found)](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wfp-layer-requirements-and-restrictions)
- [MS Q&A — Is the Windows Defender Firewall Service always necessary (BFE keeps enforcing after MpsSvc stop)](https://learn.microsoft.com/en-us/answers/questions/2111512/is-the-windows-defender-firewall-service-always-ne)
- [Quarkslab blog — WFP persistent state under the hood (registry storage, BfePersistentPolicyInit, boot-time registry path)](https://blog.quarkslab.com/windows-filtering-platform-persistent-state-under-the-hood.html) — third-party reverse-engineering, not Microsoft; used only to corroborate mechanism, not as sole source
- [GitHub bindreams/hole#998 — same question for a different Windows kill-switch app](https://github.com/bindreams/hole/issues/998) — community source, informative but not authoritative; app is architecturally different (raw WFP, not firewall rules)
- [revertservice.com — tcpip.sys driver start type](https://revertservice.com/11/tcpip/) — third-party reference site, used only for boot-start classification, consistent with general Windows driver-loading knowledge
- Repo: `src/EVBlocker.Core/Startup/StartupReconcileTask.cs:1-20`, `src/EVBlocker.Core/Startup/PolicyReconciler.cs:46-58`, `src/EVBlocker.Core/Firewall/WindowsFirewallPolicy.cs` (confirms EVBlocker only uses `INetFwPolicy2`/COM, never raw WFP — so it structurally cannot create boot-time filters today)

## Unresolved / unverified

- No single Microsoft source explicitly states "Windows Firewall's own rule-derived WFP filters
  are marked `FWPM_FILTER_FLAG_PERSISTENT`." This is inferred with high confidence from WFP
  architecture docs + the observed "stopping MpsSvc doesn't disable filtering" behavior, but it's
  an inference, not a direct citation. Would need a live boot-time test (e.g. kernel debugger or
  WFP state dump right after BFE start, before MpsSvc reports running) to fully confirm.
- Whether `FWPM_CONDITION_ALE_APP_ID` (per-application matching) is actually usable in boot-time
  filters was not found documented either way — MS's condition-availability table doesn't call out
  a boot-time-specific restriction, but boot-time enforcement happens via `netio.sys`
  (`KfdApplyBoottimePolicyCallback` per Quarkslab), and it's unclear if full ALE app-identity
  resolution is available that early. Not load-bearing for the recommendation above (raw WFP
  boot-time filters aren't recommended regardless), but would matter if this is ever revisited.
- Exact SCM start ordering/parallelism between BFE and arbitrary third-party Auto-start services on
  a specific Windows 11 build wasn't empirically measured — reasoning here is from documented start
  types/dependencies, not a boot trace.

## Addendum 2026-09-26 18:12 — measured on this machine (supersedes §1-§4 conclusions)

`netsh wfp show filters` (elevated), 871 filters, Windows 11 26200:

- EVBlocker allow filters (48) and firewall "Default Outbound" block filters (8): flags `INDEXED` / none — **not PERSISTENT**. Runtime filters, re-added by MpsSvc each boot. Report's "BFE reloads persisted firewall filters" inference is **wrong**.
- Windows "Boot Time Filter" set (desc: "in effect before the service starts"), BOOTTIME + PERSISTENT twins:
  - BLOCK only at `ALE_AUTH_RECV_ACCEPT_V4/V6` (inbound) and `IPFORWARD_V4/V6`.
  - Outbound `ALE_AUTH_CONNECT_V4/V6`: PERMIT only (FLAGS_ALL_SET, max weight). **No outbound block.**
- ⇒ "Boot time default" block = inbound only. **Outbound is open from tcpip.sys init until MpsSvc applies DefaultOutboundAction=Block + rules.** Gap real. Doc line ~304 ("có khe hở… trước khi MpsSvc áp") is correct; §4 "overstated" verdict is wrong.
- Who can use it: auto-start services / drivers starting before MpsSvc finishes; ordinary user apps start after logon, normally after MpsSvc.
- Closing it needs own WFP filters (PERSISTENT + BOOTTIME) on ALE_AUTH_CONNECT — out of reach of INetFwPolicy2 (confirmed MS: firewall rules cannot add boot-time filters).

### Re-checked 2026-09-26 18:40 with `netsh wfp show state` (916 filters, complete)

`show filters` turned out to hide filters shadowed by a heavier filter in the same sublayer, so the addendum above was re-run against the full engine state. Same result: on `ALE_AUTH_CONNECT_V4/V6` the only BOOTTIME/PERSISTENT filters are PERMITs (Boot Time Filter, Quarantine loopback exception); EVBlocker rule filters and "Default Outbound" carry no PERSISTENT flag. Gap confirmed.
