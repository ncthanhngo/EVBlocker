# Phase 03 — Safety layer

**Status:** chưa làm — phụ thuộc Phase 02
**Chạm firewall:** có (export/import config)

Phase này là **điều kiện bắt buộc** để được làm Phase 04. Không có nó, bật default-deny là tự khoá mạng máy.

## Requirements

1. Export toàn bộ config firewall trước mọi thay đổi, restore 1 click.
2. Dead-man switch: tự revert nếu người dùng không xác nhận, **sống sót qua app crash và reboot**.
3. Baseline allow-list cho OS là file JSON, sửa được không cần build lại.

## Files tạo mới

```
src/EVBlocker.Core/Safety/ConfigBackup.cs
src/EVBlocker.Core/Safety/IDeadManSwitch.cs
src/EVBlocker.Core/Safety/ScheduledTaskDeadManSwitch.cs
src/EVBlocker.Core/Baseline/OsBaseline.cs
src/EVBlocker.Core/Baseline/baseline-allow.json        # EmbeddedResource + override được từ ProgramData
tests/EVBlocker.Core.Tests/BaselineSchemaTests.cs
```

## Implementation steps

1. **ConfigBackup.** `netsh advfirewall export "<file>.wfw"` ra `%ProgramData%\EVBlocker\backups\<timestamp>.wfw`. Restore: `netsh advfirewall import`. Giữ N bản gần nhất, tự dọn bản cũ.
   Đây là chỗ duy nhất chấp nhận shell ra `netsh` — COM API không có export/import tương đương.

2. **Dead-man switch bằng Scheduled Task.** Đây là điểm cốt lõi.
   - Trước khi bật enforcement: tạo scheduled task chạy 1 lần tại `T + N` phút, action = `netsh advfirewall import <backup>`, chạy dưới `SYSTEM`.
   - Người dùng bấm "Giữ cấu hình" trong UI → app xoá task.
   - App crash / bị kill / máy reboot → task vẫn chạy → mạng tự phục hồi.
   - Timer trong process **không đủ**: chết cùng app. Đây là lý do chọn scheduled task.

3. **Baseline JSON.** Ship như EmbeddedResource, nhưng nếu tồn tại `%ProgramData%\EVBlocker\baseline-allow.json` thì ưu tiên file đó → người dùng tự bổ sung không cần chờ bản mới.

   Nội dung: danh sách service cần ra internet, scope theo **service name** chứ không theo path exe, vì chúng dùng chung `svchost.exe`:
   `Dnscache`, `Dhcp`, `wuauserv`, `BITS`, `DoSvc`, `W32Time`, `cryptsvc`, `sppsvc`, `WinDefend`, `NlaSvc`.

   Giữ nguyên các rule "Core Networking" có sẵn của Windows (DHCP, DNS, IPv6, ICMP) — không tự viết lại.

4. **Validate baseline.** Test schema: mọi entry có service name hợp lệ, không entry nào scope quá rộng (ví dụ allow trần `svchost.exe` không kèm service → cấm).

## Tests / validation

- Backup → thay đổi → restore → so sánh số rule trước/sau khớp.
- Dead-man: bật task 2 phút, **kill app**, xác nhận task vẫn chạy và config được revert.
- Dead-man: bật task, reboot, xác nhận vẫn revert.
- `BaselineSchemaTests`: từ chối entry allow `svchost.exe` không kèm service name.

## Risks / rollback

- `netsh advfirewall import` ghi đè **toàn bộ** config, kể cả rule người dùng tạo sau lúc export. Phải cảnh báo rõ trong UI trước khi restore.
- Scheduled task tạo dưới SYSTEM cần admin; nếu tạo thất bại thì **không được cho phép bật enforcement**. Fail closed.
