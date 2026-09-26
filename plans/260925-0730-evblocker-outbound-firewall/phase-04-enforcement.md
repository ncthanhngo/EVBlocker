# Phase 04 — Enforcement + UI

**Status:** code xong. Đường ghi đã verify. **Chưa bật default-deny thật lần nào** — có chủ ý không bật trên máy dev, cần máy ảo.
**Chạm firewall:** có, rủi ro cao nhất của dự án

## Lệch so với plan: trạng thái suy ra, không có state.json

Plan định lưu trạng thái vào `state.json`. Bỏ, vì trạng thái **suy ra được hoàn toàn từ máy**:

| Quan sát | Trạng thái |
|---|---|
| Không profile nào chặn | `Off` |
| Có chặn + có task revert đang chờ | `Armed` |
| Có chặn + không có task revert | `On` |

Việc chặn nằm trong config firewall, revert đang chờ nằm trong Task Scheduler — cả hai sống lâu
hơn process và đều có thể bị người khác đổi. Một file trạng thái là **ý kiến thứ hai có thể mâu
thuẫn với cả hai**, mà đúng lúc mâu thuẫn là lúc người ta cần biết sự thật. Bỏ file cũng bỏ luôn
nguy cơ file hỏng.

Hệ quả: app **không biết thời điểm revert** nếu không phải chính phiên đó arm. UI nói thẳng điều
đó thay vì bịa ra một countdown.

## Phát hiện: COM báo `Allow` ở chỗ PowerShell báo `NotConfigured`

`NET_FW_ACTION` chỉ có `Block` và `Allow` — không có `NotConfigured`. Nên COM trả về **hành vi
hiệu dụng**, còn `Get-NetFirewallProfile` trả về **cách cấu hình**. Đã đo trên máy chưa từng chỉnh:
COM = `Allow`, PowerShell = `NotConfigured`.

Hệ quả thật: `Disable()` đặt `Allow` **tường minh**, không đưa profile về `NotConfigured` được.
Trên máy độc lập thì giống hệt nhau. Trên máy do Group Policy quản, đặt giá trị cục bộ tường minh
là một thay đổi đáng biết — muốn hoàn nguyên chính xác thì phải `ConfigBackup.Restore`.

## Thứ tự trong `Enable()` chính là lập luận an toàn

```
đọc trạng thái -> nạp baseline -> BACKUP -> ghi rule -> ARM revert -> MỚI chặn
```

Chặn là bước **cuối cùng**, sau khi đường lui đã tồn tại. Process chết ở bất kỳ điểm nào sau khi
arm thì revert vẫn chạy; chết trước đó thì chưa có gì bị chặn. Có test riêng cho từng nhánh hỏng.

## Kết quả verify (2026-09-26)

| Kiểm tra | Kết quả |
|---|---|
| `dotnet test` | 177/177 pass (+18 cho Phase 04) |
| `get_DefaultOutboundAction` qua COM | đọc được không cần admin, 3/3 profile |
| `BuildDesiredRules` | 10 baseline rule, tất cả scope theo service, `ApplicationPath = null` |
| `GetStatus()` trên máy thật | `state=Off revertPending=False` — khớp thực tế |
| UI enforcement strip | render đúng, nút disabled chính xác khi không có admin |

**Đã verify (elevated, 2026-09-26):** mọi thao tác ghi mà `Enable` dựa vào — tạo/xoá rule, export
backup, đăng ký task dưới SYSTEM — đều chạy thật qua `EVBlocker.Verify`, 21/21 PASS, và
`DefaultOutboundAction` đọc đầu/cuối không đổi.

**Chưa verify:** chính `Enable`/`Confirm`/`Disable`. Chúng chỉ khác ở bước cuối là đặt
`DefaultOutboundAction = Block`, và cố ý không chạy trên máy dev — cần máy ảo.

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
