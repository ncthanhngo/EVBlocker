# EVBlocker — Outbound allow-list firewall cho Windows 10/11

**Status:** Cả 6 phase xong, đường ghi đã verify elevated (21/21). Còn lại: bật default-deny thật và test trên VM trắng.
**Ngày tạo:** 2026-09-25
**Cập nhật:** 2026-09-26

## Lệch thứ tự phase: UI làm sớm

UI vốn xếp ở Phase 04. Đã kéo lên làm ngay sau Phase 01 theo yêu cầu người dùng, ở dạng
**chỉ đọc**: hiện dữ liệu Phase 01, không có nút nào ghi vào firewall. Enforcement vẫn ở Phase 04
và vẫn bị chặn sau Phase 03.

Giao diện dùng lại design system của SmartLab (cùng nhà EVSELab): `Tokens.xaml`,
`Palette.Dark/Light.xaml`, `Logo.xaml` copy nguyên vẹn vào repo để EVBlocker vẫn standalone,
không tham chiếu chéo sang project khác. `Controls.xaml` viết riêng, chỉ những control app này
dùng — copy cả 40KB của SmartLab đồng nghĩa với việc bảo trì template không có gì render.

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
| Firewall API | COM `INetFwPolicy2`, interop sinh bằng **CsWin32** | Vtable đến từ metadata Win32 chính thức, không từ trí nhớ. Xem phase-02 |
| Active connections | P/Invoke `GetExtendedTcpTable` / `GetExtendedUdpTable` | Không spawn process, đủ nhanh để poll 1s |
| History | Event Log 5157/5156 qua `EventLogQuery` | Dữ liệu sẵn có, không cần driver |
| Đóng gói | Self-contained single-file, **không trim** (62.9 MB) | WPF không hỗ trợ trimming (NETSDK1168) — xem phase-06 |
| Service | Không (v1) | YAGNI — rule firewall vẫn hiệu lực khi đóng app |
| Giao diện | Sáng (mặc định) + tối, đổi trong Cài đặt | Hai palette key-for-key, swap runtime |
| Cập nhật | Kiểm tra GitHub Releases **khi bấm nút** | App này ngăn phần mềm tự ra internet; nó không tự làm điều đó |

Không dùng driver/WFP callout: không cần cho yêu cầu hiện tại, và ký driver là rào cản lớn.

## Kiến trúc

```
src/EVBlocker.Core/     class library, không phụ thuộc UI  -> unit test được
  Firewall/   IFirewallPolicy + impl COM, rule model
  Monitor/    ActiveConnectionScanner, BlockedAttemptReader
  Policy/     AllowListStore, PolicyApplier
  Baseline/   OsBaseline + baseline-allow.json (data, không hardcode)
  Safety/     ConfigBackup, DeadManSwitch
  Startup/    SchTasksHost, StartupReconcileTask, PolicyReconciler  # áp lại rule khi boot
  Updates/    ReleaseVersion  # so tag GitHub với version đang chạy
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
| [05](phase-05-startup-persistence.md) | Reconciler chạy lúc boot, xử lý reboot (bỏ tray) | Có |
| [06](phase-06-packaging.md) | Self-contained single-file (không trim được), release workflow | Không |

**Dependency:** 01 → 02 → 03 → 04 → 05. Phase 04 **không được làm trước** 03 — bật default-deny mà chưa có đường rollback là tự khoá mạng của máy.

06 làm được song song sau 02.

## Tài liệu

Plan này ghi lại **cách dự án được dựng** — các phase, chỗ lệch so với dự kiến, và kết quả kiểm
chứng. Còn **sản phẩm là gì** thì ở `docs/`:

- [`docs/huong-dan-su-dung.md`](../../docs/huong-dan-su-dung.md) — cho người dùng
- [`docs/system-architecture.md`](../../docs/system-architecture.md) — cho người bảo trì

## Kiểm chứng đường ghi

Các thao tác cần quyền admin không unit test được, nên Phase 02-04 đều ghi "chưa verify". Công cụ
đóng khoảng trống đó:

```powershell
# PowerShell chạy as Administrator
.\tools\EVBlocker.Verify\bin\Debug\net8.0-windows\EVBlocker.Verify.exe
```

Kiểm tạo/đọc/xoá rule, `netsh advfirewall export`, và đăng ký task dưới SYSTEM. **Không bật
default-deny** — đọc `DefaultOutboundAction` ở đầu và cuối, khác nhau là FAIL. Mỗi thao tác ghi
được xác nhận độc lập qua PowerShell, không đọc lại bằng chính code vừa ghi.

Là console app chứ không phải `.ps1` vì PowerShell 5.1 không nạp được assembly .NET 8 — script
sẽ phải gọi `netsh`/`schtasks` trực tiếp, tức là kiểm chứng Windows chứ không kiểm chứng code.

## Acceptance criteria (toàn dự án)

Đã kiểm chứng bằng thực nghiệm:

- [x] Scan liệt kê đúng process đang có kết nối ra IP public — đối chiếu `Get-NetTCPConnection`, 37/37 dòng khớp, 0 dòng lạ
- [x] History parse được event 5157/5156 và map path kernel sang ổ đĩa — test với XML mẫu
- [x] `dotnet test` xanh (227), `dotnet build` 0 warning
- [x] Build self-contained ≤ 80 MB — **62.9 MB**, đúng 1 file, chạy được cả UI lẫn `--reconcile`
- [x] Sửa/xoá rule → reconciler phát hiện và áp lại — test với fake; đường chạy thật của `--reconcile` đã verify (thất bại an toàn khi thiếu quyền)

Đã kiểm chứng bằng `tools/EVBlocker.Verify` chạy elevated (2026-09-26, 21/21 PASS):

- [x] Allow-list thêm/xoá app phản ánh đúng vào Windows Firewall — PowerShell xác nhận độc lập rule xuất hiện rồi biến mất
- [x] Có backup config trước mọi thay đổi — `netsh advfirewall export` tạo được file, `List()` tìm thấy
- [x] Đăng ký scheduled task dưới SYSTEM, `StartWhenAvailable=True`, huỷ được — PowerShell xác nhận độc lập
- [x] Không thao tác nào làm đổi `DefaultOutboundAction` (đọc đầu/cuối, khớp)

Chưa kiểm được — **cần máy ảo**:

- [ ] Bật default-deny xong, baseline vẫn hoạt động: DNS, Windows Update, Defender signature, activation, time sync
- [ ] Dead-man switch tự revert **kể cả khi app bị kill hoặc máy reboot**
- [ ] Sau reboot, policy vẫn hiệu lực mà không cần mở app
- [ ] Bản self-contained chạy trên máy Windows trắng (không cài .NET)

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
