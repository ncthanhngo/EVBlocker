# Phase 04 — Enforcement + UI

**Status:** chưa làm — **phụ thuộc Phase 03, không được làm trước**
**Chạm firewall:** có, rủi ro cao nhất của dự án

## Requirements

1. Bật/tắt `DefaultOutboundAction = Block` cho cả 3 profile.
2. Bắt buộc đi qua quy trình an toàn: backup → áp baseline → dead-man switch → mới bật.
3. UI hoàn chỉnh: 3 tab (Đang kết nối / Đã bị chặn / Allow-list) + nút enforcement.

## Files tạo mới

```
src/EVBlocker.Core/Firewall/EnforcementController.cs
src/EVBlocker.App/App.xaml(.cs)
src/EVBlocker.App/app.manifest                       # requireAdministrator
src/EVBlocker.App/Views/MainWindow.xaml
src/EVBlocker.App/Views/ActiveConnectionsView.xaml
src/EVBlocker.App/Views/BlockedHistoryView.xaml
src/EVBlocker.App/Views/AllowListView.xaml
src/EVBlocker.App/Views/EnforcementBar.xaml          # trạng thái + countdown dead-man
src/EVBlocker.App/ViewModels/*.cs
src/EVBlocker.App/Resources/Strings.vi.resx          # UI tiếng Việt, tách resource để đổi ngôn ngữ sau
tests/EVBlocker.Core.Tests/EnforcementControllerTests.cs
```

## Implementation steps

1. **EnforcementController** — state machine, fail closed. Không cho bật nếu bất kỳ tiền đề nào thiếu:

   ```
   Off -> (backup OK? baseline applied? dead-man task created?) -> Armed -> (user confirm) -> On
                         thiếu bất kỳ cái nào -> từ chối, giữ Off
   ```

   `Armed` = đã bật block nhưng dead-man đang đếm. Nếu hết giờ không confirm → tự về `Off`.

2. **UI 3 tab.**
   - *Đang kết nối*: bảng process đang mở kết nối ra IP public. Poll 1s, `ObservableCollection` cập nhật theo diff (không clear rồi add lại — tránh nháy và GC áp lực).
   - *Đã bị chặn*: group theo exe, cột số lần + lần cuối. Nút "Cho phép" thêm thẳng vào allow-list.
   - *Allow-list*: danh sách app được phép, thêm/xoá, cảnh báo hash lệch.

3. **Virtualization.** `DataGrid` bật `EnableRowVirtualization` + `VirtualizingStackPanel.IsVirtualizing`. Danh sách có thể vài nghìn dòng.

4. **Cảnh báo interpreter.** Khi người dùng allow `powershell.exe`, `cmd.exe`, `curl.exe`, `python.exe`, `node.exe`, `wscript.exe`, `mshta.exe`, `rundll32.exe`: hiện cảnh báo rõ rằng cho phép chúng = cho phép mọi thứ chạy qua chúng.

5. **Manifest** `requireAdministrator` — UAC 1 lần khi mở app, không cần elevate lẻ từng thao tác.

## Tests / validation

- `EnforcementControllerTests`: từ chối bật khi thiếu backup / baseline / dead-man. Test bằng fake, không cần admin.
- Sau khi bật default-deny, xác minh **từng mục** còn hoạt động: `nslookup`, Windows Update check, Defender signature update, `w32tm /resync`, activation status.
- Kill app lúc `Armed` → mạng tự phục hồi sau N phút.
- UI: 5000 dòng giả, kiểm tra scroll không giật.

## Risks / rollback

| Rủi ro | Xử lý |
|---|---|
| Baseline thiếu → mất mạng hoàn toàn | Dead-man switch + backup. Không bao giờ bật mà chưa qua Phase 03 |
| Mất kết nối VPN/remote đang dùng | Cảnh báo trước khi bật nếu phát hiện session remote đang hoạt động |
| Người dùng bấm bật rồi đi khỏi máy | Dead-man tự revert, đó chính là mục đích |

Rollback: `netsh advfirewall import <backup>`, hoặc `Set-NetFirewallProfile -DefaultOutboundAction NotConfigured`.
