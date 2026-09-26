# Phase 05 — Startup, persistence, xử lý reboot

**Status:** chưa làm — phụ thuộc Phase 04
**Chạm firewall:** có (áp lại policy khi phát hiện drift)

## Tiền đề cần hiểu đúng

Việc chặn **không do app thực hiện**. Nó do `MpsSvc` (Windows Firewall service) áp từ config
đã lưu. `MpsSvc` là service khởi động sớm, trước mọi phần mềm user-mode. Nên:

- Sau reboot, policy **tự có hiệu lực**, không cần app chạy.
- Không cần app "chạy trước các phần mềm khác" — OS đã làm việc đó ở tầng thấp hơn app.

Phase này giải quyết 2 việc khác: **drift** (config bị sửa ngoài app) và **trạng thái sau reboot**.

## Requirements

1. Task chạy lúc boot, so sánh policy thực tế vs policy mong muốn, áp lại nếu lệch.
2. Xử lý đúng mọi tổ hợp reboot × trạng thái enforcement.
3. Tray icon (tùy chọn) hiện trạng thái, mở UI nhanh.
4. Ghi log mỗi lần reconcile để truy vết được.

## Files tạo mới

```
src/EVBlocker.Core/Startup/IScheduledTaskHost.cs
src/EVBlocker.Core/Startup/TaskSchedulerHost.cs        # tạo/xoá/kiểm tra task qua COM Task Scheduler 2.0
src/EVBlocker.Core/Startup/PolicyReconciler.cs         # diff desired vs actual, áp lại
src/EVBlocker.Core/Startup/ReconcileReport.cs
src/EVBlocker.App/Tray/TrayIconHost.cs
tests/EVBlocker.Core.Tests/PolicyReconcilerTests.cs
```

## Implementation steps

### 1. Startup task — vì sao Scheduled Task, không phải Run key

| Cách | Vấn đề |
|---|---|
| `HKCU/HKLM ...\Run` | Chạy lúc **logon**, sau khi nhiều thứ đã start. Chạy quyền user → cần UAC → popup hoặc fail |
| Startup folder | Y như trên, còn dễ bị user xoá |
| Windows Service | Làm được, nhưng v1 đã quyết không dùng service (thêm installer, thêm bề mặt lỗi) |
| **Scheduled Task, trigger At startup, user SYSTEM, RunLevel Highest** | Chạy **trước logon**, không UAC, không cần session người dùng. Chọn cái này |

Task chạy chính exe với switch `--reconcile` (chế độ không UI, exit code phản ánh kết quả).
Tạo task qua COM Task Scheduler 2.0 (`ITaskService`), không shell ra `schtasks` để tránh parse text.

Bật `StartWhenAvailable = true` để task chạy bù nếu bị bỏ lỡ.

### 2. Chế độ `--reconcile` (headless)

```
đọc allowlist.json + baseline-allow.json  (desired)
đọc rule group EVBlocker + DefaultOutboundAction  (actual)
diff
  khớp        -> log "no drift", exit 0
  lệch        -> áp lại, log chi tiết cái gì lệch, exit 0
  không đọc được desired -> KHÔNG làm gì, log lỗi, exit != 0
```

Nguyên tắc: **desired không đọc được thì không thay đổi gì**. Thà giữ nguyên còn hơn áp một
policy rỗng rồi chặn sạch hoặc mở sạch.

### 3. Ma trận reboot × trạng thái

| Trạng thái trước reboot | Sau reboot phải làm gì |
|---|---|
| `Off` | Không làm gì |
| `Armed` (dead-man đang đếm) | Dead-man task là one-time tại `T+N`, có `StartWhenAvailable` → chạy bù, revert. Reconciler thấy trạng thái `Armed` mà không có confirm → **không** áp lại enforcement |
| `On` (đã confirm) | Policy đã tự hiệu lực. Reconciler chỉ kiểm drift và áp lại phần lệch |

Trạng thái lưu ở `%ProgramData%\EVBlocker\state.json`, ghi bằng write-tạm-rồi-rename để không hỏng file khi mất điện giữa lúc ghi.

### 4. Tray icon

Chỉ là tiện lợi, không phải cơ chế thực thi. Chạy lúc logon qua task riêng (trigger At logon,
quyền user thường — tray không cần admin vì chỉ đọc trạng thái và mở UI).

## Tests / validation

| Kiểm tra | Cách |
|---|---|
| `PolicyReconcilerTests` | fake `IFirewallPolicy`: không drift → không gọi write; drift → gọi đúng số lần; desired lỗi → không gọi write |
| Drift thật | xoá 1 rule bằng `wf.msc`, chạy `--reconcile`, xác nhận rule quay lại |
| Reboot ở `On` | reboot, kiểm `DefaultOutboundAction` vẫn `Block` **trước khi** mở app |
| Reboot ở `Armed` | reboot, xác nhận tự revert, không tự bật lại enforcement |
| state.json hỏng | ghi file rác, chạy `--reconcile`, xác nhận không thay đổi gì và exit != 0 |

## Risks / rollback

- Task chạy SYSTEM áp policy sai → mất mạng ngay từ boot, khó sửa. Giảm rủi ro: reconciler
  **chỉ áp lại đúng desired đã lưu**, không tự suy diễn; và luôn giữ backup `.wfw` gần nhất.
- Task bị disable/xoá → mất khả năng chống drift nhưng **không mất enforcement**. UI phải hiện
  cảnh báo nếu task không tồn tại.
- Rollback: xoá task + `netsh advfirewall import <backup>`.
