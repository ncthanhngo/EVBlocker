# Phase 2 — Gắn vào vòng đời bật/tắt chặn

## Context
- `src/EVBlocker.Core/Firewall/EnforcementController.cs` (Enable/Disable/Confirm)
- `src/EVBlocker.Core/Safety/ScheduledTaskDeadManSwitch.cs` (revert = `netsh advfirewall import`)
- `src/EVBlocker.Core/Startup/StartupReconcileTask.cs`, `PolicyReconciler.cs`, `src/EVBlocker.App/App.xaml.cs` (`--reconcile`)

## Requirements
- **Enable**: sau khi rule đã ghi và dead-man đã armed, trước khi đặt Block: cài exe cố định +
  task khởi động (bắt buộc, không còn là tuỳ chọn khi có khoá), rồi `BootGuard.Install()`.
- **Disable**: đặt Allow, rồi `BootGuard.Remove()`.
- **Dead-man revert**: task thêm action thứ hai `EVBlocker.exe --remove-boot-guard` (exe cố định).
  Revert phải gỡ khoá, nếu không máy mất mạng ở lần boot sau.
- **Boot (`--reconcile`, SYSTEM)**: nếu có khoá ⇒ chờ MpsSvc Running và đọc được
  `DefaultOutboundAction` (poll 1 s, tối đa 3 phút) ⇒ reconcile rule như cũ ⇒ `Release()`. Hết giờ
  ⇒ vẫn `Release()` và ghi log rõ "mở chốt khi firewall chưa sẵn sàng". Mọi exception ⇒ vẫn Release.
- **Exe cố định**: khi Enable, copy exe đang chạy vào `%ProgramFiles%\EVBlocker\EVBlocker.exe`
  (ghi đè nếu khác), task trỏ vào đó. Xử lý luôn việc task SYSTEM đang chạy exe trong thư mục
  người dùng.
- Task khởi động: thêm `<Priority>` cao và trigger BootTrigger không delay.

## Files
- Sửa: `EnforcementController.cs`, `ScheduledTaskDeadManSwitch.cs` (+ test XML), `StartupReconcileTask.cs`,
  `App.xaml.cs` (switch `--remove-boot-guard`), `CoreServices.cs`, `EnforcementViewModel.cs` (task bắt buộc
  khi đang chặn: nút tắt tự kiểm tra bị khoá, kèm lý do).
- Tạo: `src/EVBlocker.Core/Startup/FixedInstall.cs` (copy exe vào Program Files).

## Steps
1. `IBootGuard` vào `EnforcementController` qua constructor; fake cho test.
2. Thứ tự Enable: baseline → backup → rules → arm dead-man → fixed install + task → guard → Block.
3. Test: Enable gọi Install trước Block; Disable gọi Remove; lỗi Install ⇒ không đặt Block.
4. Dead-man XML có 2 action; test khẳng định action gỡ khoá tồn tại.
5. Reconcile: test chờ/timeout với đồng hồ giả.

## Validation
- Unit test; Verify tool chạy Enable/Disable trên máy thật với revert 1 phút.

## Risks / rollback
- Thứ tự sai ⇒ mất mạng. Test khẳng định thứ tự bằng fake ghi lại chuỗi lời gọi.
- Rollback code: revert commit; gỡ khoá trên máy bằng phase 3.
