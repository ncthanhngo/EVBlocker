# Phase 02 — Allow-list store + rule engine

**Status:** chưa làm — phụ thuộc Phase 01
**Chạm firewall:** có, nhưng **chỉ thêm/xoá rule**. Không đổi `DefaultOutboundAction`.

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
