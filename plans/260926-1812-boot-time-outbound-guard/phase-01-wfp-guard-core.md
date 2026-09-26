# Phase 1 — Lõi WFP

## Context
- [plan.md](plan.md), [report + addendum](../reports/researcher-260926-1800-boot-time-outbound-gap.md)
- Interop hiện có: `src/EVBlocker.Core/NativeMethods.txt` (CsWin32), `Firewall/WindowsFirewallPolicy.cs`
- MS: [WFP Operation](https://learn.microsoft.com/en-us/windows/win32/fwp/basic-operation), [Filter Arbitration](https://learn.microsoft.com/en-us/windows/win32/fwp/filter-arbitration), `FWPM_FILTER0`

## Requirements
- Provider + sublayer riêng, GUID cố định (hằng số), PERSISTENT.
- `BootGuard.Install()`: trong **một transaction**, thêm chốt mở trước rồi mới thêm khoá, để không
  có khoảnh khắc nào khoá có hiệu lực khi máy đang chạy.
- `BootGuard.Release()`: thêm chốt mở (idempotent — đã có thì thôi).
- `BootGuard.Remove()`: xoá mọi filter theo key, rồi sublayer, rồi provider. Idempotent.
- `BootGuard.GetStatus()`: Installed / Released / Absent — đọc từ WFP, không lưu file.
- Khoá: block `ALE_AUTH_CONNECT_V4/V6`. Cho qua: loopback (`FLAGS` IS_LOOPBACK), DHCP
  (UDP remote 67 v4, 547 v6).

## Files
- Sửa: `src/EVBlocker.Core/NativeMethods.txt` — `FwpmEngineOpen0/Close0`, `FwpmTransaction*0`,
  `FwpmProviderAdd0/DeleteByKey0`, `FwpmSubLayerAdd0/DeleteByKey0`, `FwpmFilterAdd0/DeleteByKey0/GetByKey0`, `FwpmFreeMemory0`, hằng số `FWPM_LAYER_*`, `FWPM_CONDITION_*`.
- Tạo: `src/EVBlocker.Core/Firewall/BootGuard.cs` (vận hành), `BootGuardFilters.cs` (dựng danh
  sách filter — thuần, test được), `IBootGuard.cs`.
- Tạo test: `tests/.../BootGuardFiltersTests.cs`.

## Steps
1. Spike (admin, trong tools/EVBlocker.Verify): thêm 1 filter BOOTTIME vào sublayer riêng. Nếu
   BFE từ chối ⇒ đổi phương án như ghi ở plan.md.
2. Viết `BootGuardFilters`: key cố định cho từng filter, weight: chốt mở > cho qua > khoá.
3. Viết `BootGuard` với transaction; lỗi giữa chừng ⇒ abort, không để nửa vời.
4. Unit test: đủ filter cho v4 + v6; chốt mở không persistent; khoá có cả BOOTTIME và PERSISTENT;
   thứ tự weight đúng.

## Validation
- Unit test mới + 353 test cũ.
- Verify tool (admin): Install → kết nối TCP ra 1.1.1.1:443 vẫn được; gỡ chốt mở → kết nối bị
  chặn, loopback vẫn được; Release → được lại; Remove → `netsh wfp show state` sạch.

## Risks / rollback
- Bug khiến chốt mở không thêm được ⇒ mất mạng ngay. Verify tool luôn `Remove()` trong `finally`;
  chạy trên máy này có người ngồi trước màn hình.

## Kết quả (2026-09-26)

Xong. Files: `Firewall/BootGuard.cs`, `BootGuardFilters.cs`, `IBootGuard.cs`, `NativeMethods.txt`,
`tests/.../BootGuardFiltersTests.cs` (9 test), `tools/EVBlocker.Verify` (bước kiểm khoá).

Verify tool, elevated, chạy qua `dotnet.exe` (có trong allow-list) trên máy đang bật chặn: **34/34**.
- Cài khoá + chốt mở: vẫn ra internet (1.1.1.1:443).
- Gỡ chốt mở: không ra được internet; loopback vẫn thông. Mở chốt: ra lại được.
- `netsh wfp show state`: 2 block BOOTTIME, 2 block PERSISTENT, 2 release không persistent, 14 filter.
- Gỡ: 0 filter, không còn sublayer/provider. `DefaultOutboundAction` không đổi.

Phát hiện khi kiểm:
- BOOTTIME filter dùng được provider + sublayer riêng (câu hỏi mở đã đóng).
- `netsh wfp show filters` (kể cả `verbose=on`) **không** liệt kê filter bị filter nặng hơn không
  điều kiện trong cùng sublayer che — khi khoá đang mở chốt, nó chỉ thấy 2 filter release. Dùng
  `netsh wfp show state`. File đó có 2 phần tử gốc (`wfpstate`, `firewallState`), phải bọc lại
  trước khi parse. Ghi vào tài liệu khôi phục ở phase 3: bảo người dùng dùng `show state`.
- Chưa kiểm được DHCP thật (cần reboot hoặc gia hạn lease khi khoá bật) — để phase 4.
