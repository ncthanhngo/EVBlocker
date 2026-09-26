# Phase 02 — Allow-list store + rule engine

**Status:** xong. Nhánh đọc đã verify thực nghiệm; **nhánh ghi chưa chạy được** (cần admin).
**Chạm firewall:** có, nhưng **chỉ thêm/xoá rule**. Không đổi `DefaultOutboundAction`.

## Lệch so với plan: dùng CsWin32 thay vì `[GeneratedComInterface]`

Plan ban đầu chọn `[GeneratedComInterface]`. Đổi vì phát hiện thêm bằng chứng:

`INetFwPolicy2` và `INetFwRule` là **dual interface** — vtable bắt đầu bằng IUnknown rồi IDispatch,
sau đó là **mọi** getter/setter theo đúng thứ tự IDL. Viết tay nghĩa là chỉ cần lệch một slot là
gọi nhầm hàm, mà nhánh ghi **không test được nếu không có admin** — lỗi sẽ lộ trên máy người dùng
chứ không phải ở đây.

`Microsoft.Windows.CsWin32` sinh interop từ metadata Win32 chính thức, `PrivateAssets=all` nên chỉ
chạy lúc build, không thành dependency runtime. Nó cũng bắt được một lỗi thật: CLSID tôi nhớ là
`E2B3C97F-6AE1-41AC-817A-F524F22CFCE2`, giá trị đúng là `...-F6F92166D7DD`.

Đánh đổi: CsWin32 phơi BSTR dạng con trỏ thô, nên mỗi get/set chuỗi phải tự cấp phát và giải
phóng. Gom vào đúng 2 helper (`ReadBstr`/`WriteBstr`) để không sót chỗ nào.

## Kết quả verify (2026-09-26)

| Kiểm tra | Kết quả |
|---|---|
| `dotnet test` | 112/112 pass (thêm 33 test cho Phase 02) |
| Đếm rule COM vs `Get-NetFirewallRule` | 52=52 và 40=40 |
| **So tập tên rule** nhóm `@FirewallAPI.dll,-25000` | **40/40 khớp, 0 lệch hai chiều** |
| Map field | Direction, Action, Enabled, ApplicationName, ServiceName, Profiles đều đúng |
| `Profiles` của rule "all" | đọc ra `2147483647` → xác nhận `FirewallProfiles.All = 0x7FFFFFFF` đúng, không phải `Domain\|Private\|Public = 7` |
| Rule scope theo service | đọc đúng `svc=dhcp`, `svc=gpsvc`, `svc=upnphost` — cần cho baseline Phase 03 |
| End-to-end store → applier → COM | `Plan()` trên allow-list thật trả `add=2 remove=0`, tên rule có digest đúng |

**Chưa verify:** `AddRule` / `RemoveRule`. Cả hai dùng chung interface đã sinh và các setter đối
xứng với getter đã chứng minh, nhưng chưa có lần chạy thật nào.

## Requirements

1. Lưu allow-list của người dùng bền vững, có versioning để nâng cấp schema.
2. Tạo/xoá outbound Allow rule trong Windows Firewall qua COM.
3. Idempotent: apply 2 lần không sinh rule trùng.
4. Reconcile: phát hiện rule bị người khác sửa/xoá ngoài app và báo lệch.

## Files tạo mới

```
src/EVBlocker.Core/Firewall/IFirewallPolicy.cs
src/EVBlocker.Core/Firewall/NetFwComInterop.cs        # [GeneratedComInterface] INetFwPolicy2/INetFwRule(s)
src/EVBlocker.Core/Firewall/WindowsFirewallPolicy.cs
src/EVBlocker.Core/Firewall/FirewallRuleSpec.cs
src/EVBlocker.Core/Policy/AllowedApp.cs
src/EVBlocker.Core/Policy/AllowListStore.cs
src/EVBlocker.Core/Policy/PolicyApplier.cs
tests/EVBlocker.Core.Tests/PolicyApplierTests.cs      # dùng IFirewallPolicy giả
```

## Implementation steps

1. **COM interop.** Khai báo `INetFwPolicy2`, `INetFwRules`, `INetFwRule` bằng `[GeneratedComInterface]` của .NET 8 (source-generated, trim/AOT-friendly). Không dùng `dynamic` + `Type.GetTypeFromCLSID`: kéo theo `Microsoft.CSharp` và phá trimming.
   - Fallback nếu interop quá tốn công: `netsh advfirewall` qua process. Chỉ dùng khi bí — chậm và phải parse text.

2. **Rule naming.** Tất cả rule gắn `Grouping = "EVBlocker"` để quản theo nhóm và không lẫn rule hệ thống. Display name: `EVBlocker - Allow - <app> - <exe>`.

3. **AllowListStore.** JSON ở `%ProgramData%\EVBlocker\allowlist.json` (không phải `%AppData%` — policy áp cho cả máy, không riêng user). Có field `schemaVersion` để migrate.

4. **AllowedApp model.** Lưu path exe + SHA-256 + tên hiển thị + thời điểm thêm. Hash để phát hiện exe bị thay thế sau update — cảnh báo, không tự chặn.

5. **PolicyApplier.** So sánh desired (allow-list) vs actual (rule trong group `EVBlocker`), sinh diff, apply tối thiểu. Thuần logic trên `IFirewallPolicy` → test được không cần admin.

## Tests / validation

- `PolicyApplierTests` với `IFirewallPolicy` giả: diff đúng, idempotent, phát hiện rule lệch.
- Kiểm chứng thủ công: thêm 1 app, mở `wf.msc`, xác nhận rule xuất hiện đúng group/scope/profile.
- Round-trip: apply → xoá → apply lại, không sinh rule trùng.

## Risks / rollback

- Rule tạo sai scope (thiếu profile) → allow không có tác dụng ở Public. Luôn set `Profiles = All`.
- Xoá nhầm rule hệ thống → **chỉ được xoá rule có `Grouping == "EVBlocker"`**, assert trước khi xoá.
- Rollback: xoá toàn bộ group `EVBlocker`.
