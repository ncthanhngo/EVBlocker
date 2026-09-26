# EVBlocker — Outbound allow-list firewall cho Windows 10/11

**Status:** Phase 01 code xong + verified. Tiếp theo: Phase 02.
**Ngày tạo:** 2026-09-25
**Cập nhật:** 2026-09-25

## Mục tiêu

Ứng dụng desktop cho phép:

1. Chặn toàn bộ outbound internet theo mặc định (`DefaultOutboundAction = Block`).
2. Giữ các service và thành phần tối thiểu của Windows vẫn ra được internet.
3. Chỉ app trong allow-list của người dùng được ra internet.
4. Scan: app nào **đang** mở kết nối ra internet.
5. History: app nào **đã từng** cố kết nối và bị chặn.
6. Chính sách sống sót qua reboot, và tự áp lại nếu bị sửa/xoá bên ngoài app.

## Điểm quan trọng về persistence

Enforcement **không cần app chạy**. `DefaultOutboundAction` và rule nằm trong config của
Windows Firewall, do service `MpsSvc` áp. `MpsSvc` khởi động rất sớm trong boot — trước mọi
phần mềm user-mode — nên chặn có hiệu lực ngay sau reboot dù app không bao giờ được mở.

Thành phần startup **không phải** để thực thi việc chặn, mà để:

- phát hiện config bị thay đổi ngoài app (installer, GPO, tool khác, Windows reset) và áp lại
- xử lý trạng thái sau reboot khi dead-man switch đang đếm

Hệ quả: không cần Run key hay Startup folder (chạy lúc logon, sau khi nhiều thứ đã khởi động,
và cần UAC). Dùng **Scheduled Task trigger At startup, chạy dưới SYSTEM** — chạy trước logon,
không popup UAC.

## Quyết định công nghệ

| Hạng mục | Chọn | Lý do |
|---|---|---|
| Runtime | .NET 8 (SDK 8.0.423 có sẵn) | WindowsDesktop runtime đã có trên máy |
| UI | WPF + MVVM | Native, mượt, không cần webview |
| Firewall API | COM `INetFwPolicy2` qua `[GeneratedComInterface]` | Nhanh, type-safe, trim-friendly. Không shell ra netsh/PowerShell |
| Active connections | P/Invoke `GetExtendedTcpTable` / `GetExtendedUdpTable` | Không spawn process, đủ nhanh để poll 1s |
| History | Event Log 5157/5156 qua `EventLogQuery` | Dữ liệu sẵn có, không cần driver |
| Đóng gói | Self-contained single-file, trimmed (~65 MB) | Chạy trên máy trắng, không cần cài runtime |
| Service | Không (v1) | YAGNI — rule firewall vẫn hiệu lực khi đóng app |

Không dùng driver/WFP callout: không cần cho yêu cầu hiện tại, và ký driver là rào cản lớn.

## Kiến trúc

```
src/EVBlocker.Core/     class library, không phụ thuộc UI  -> unit test được
  Firewall/   IFirewallPolicy + impl COM, rule model
  Monitor/    ActiveConnectionScanner, BlockedAttemptReader
  Policy/     AllowListStore, PolicyApplier
  Baseline/   OsBaseline + baseline-allow.json (data, không hardcode)
  Safety/     ConfigBackup, DeadManSwitch
  Startup/    TaskSchedulerHost, PolicyReconciler   # áp lại policy khi boot nếu bị drift
  Audit/      AuditPolicyManager
src/EVBlocker.App/      WPF, chỉ gọi Core qua interface
tests/EVBlocker.Core.Tests/
```

Mọi thành phần chạm Windows API đều nằm sau interface để test được mà không cần quyền admin.

Baseline allow-list là **file JSON**, không phải code — nâng cấp danh sách không cần build lại.

## Phases

| Phase | Nội dung | Chạm firewall? |
|---|---|---|
| [01](phase-01-scan-and-history.md) | Scaffold + scan active connections + history bị chặn + bật audit | Không (chỉ audit policy) |
| [02](phase-02-allowlist-engine.md) | Allow-list store + tạo/xoá rule qua COM | Có (chỉ thêm rule, không đổi default) |
| [03](phase-03-safety-layer.md) | Backup/restore, dead-man switch, baseline JSON | Có |
| [04](phase-04-enforcement.md) | Bật `DefaultOutboundAction = Block` + UI hoàn chỉnh | Có, rủi ro cao |
| [05](phase-05-startup-persistence.md) | Reconciler chạy lúc boot, xử lý reboot, tray | Có |
| [06](phase-06-packaging.md) | Self-contained single-file, trim, kiểm dung lượng | Không |

**Dependency:** 01 → 02 → 03 → 04 → 05. Phase 04 **không được làm trước** 03 — bật default-deny mà chưa có đường rollback là tự khoá mạng của máy.

06 làm được song song sau 02.

## Acceptance criteria (toàn dự án)

- [ ] Scan liệt kê đúng process đang có kết nối ra IP public, kèm đường dẫn exe
- [ ] History đọc được event 5157, group theo exe, đếm số lần bị chặn
- [ ] Allow-list thêm/xoá app phản ánh đúng vào Windows Firewall, kiểm chứng được bằng `wf.msc`
- [ ] Bật default-deny xong, các thành phần trong baseline vẫn hoạt động: DNS, Windows Update, Defender signature, activation, time sync
- [ ] Có backup config trước mọi thay đổi + restore 1 click
- [ ] Dead-man switch tự revert được **kể cả khi app bị kill hoặc máy reboot**
- [ ] Sau reboot, policy vẫn hiệu lực mà **không cần mở app**
- [ ] Sửa/xoá rule bên ngoài app → reconciler lúc boot phát hiện và áp lại
- [ ] `dotnet test` xanh; `dotnet build` không warning mới
- [ ] Build self-contained ≤ 80 MB

## Rủi ro đã biết

| Rủi ro | Xử lý |
|---|---|
| Bật default-deny sai baseline → mất mạng, Windows Update/Defender chết | Audit-first: quan sát trước, dựng allow-list, rồi mới bật. Bắt buộc qua Phase 03 |
| App crash giữa lúc đang enforce → không ai revert | Dead-man switch bằng **Scheduled Task**, sống sót qua crash và reboot |
| Nhiều thành phần OS chạy trong `svchost.exe` chung | Rule scope theo **service name**, không theo path exe |
| Tên subcategory của auditpol bị bản địa hoá | Dùng **GUID** subcategory, không dùng tên |
| Cho phép `powershell.exe` / `curl.exe` = cho phép mọi thứ đi qua nó | UI phải cảnh báo rõ khi allow các interpreter/tool này |
| App Store/UWP không có path exe | Ghi nhận giới hạn; hỗ trợ package SID để sau |
| Process chạy quyền admin tự xoá được rule | Reconciler lúc boot áp lại. Ghi rõ trong docs: đây không phải hàng rào chống malware |
| Config bị installer/GPO/Windows reset ghi đè | Reconciler so sánh actual vs desired mỗi lần boot, áp lại nếu lệch |
| Khe hở rất sớm trong boot, trước khi `MpsSvc` áp policy đầy đủ | Cố hữu của Windows (boot-time WFP filter), không kiểm soát được. Ghi nhận trong docs, không hứa hẹn sai |

## Giới hạn có chủ ý (không làm)

- Không lọc theo domain/URL — Windows Firewall không làm được việc đó.
- Không chặn được app ẩn trong `svchost`/`rundll32` ở mức chi tiết hơn service name.
- Không có popup hỏi real-time (quyết định v1, xem lại ở v2 nếu cần).
