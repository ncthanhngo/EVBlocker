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

## Kết quả (2026-09-26)

Xong. Khác plan:
- Tường lửa "sẵn sàng" = đọc được `DefaultOutboundAction` (COM chỉ trả lời khi MpsSvc chạy), không
  gọi ServiceController — tránh thêm package.
- Thêm `SyncBootGuard(prepare)`: dùng lúc boot (prepare=false) và khi app mở bằng quyền admin
  (prepare=true) — cài bù cho máy đã bật chặn từ trước khi có khoá (chính máy này).
- App mở bằng quyền admin cũng làm mới bản exe cố định nếu task đang dùng nó, để bản đó không cũ.

Test: 19 test mới (`BootGuardLifecycleTests`), task khởi động kiểm schema thật với Task Scheduler.

Tích hợp thật, elevated, bản Release, trên máy đang bật chặn:
1. Mở app: chép exe + script vào Program Files, task trỏ vào đó, không delay, 14 filter khoá.
2. Gỡ chốt mở (giả lập boot) → `--reconcile` → chốt mở về, log `Boot guard: released`.
3. Script khôi phục → 0 filter.
4. `--reconcile` lần nữa → khoá cài lại (đang chặn).

## Review (2026-09-26) — [report](../reports/code-reviewer-260926-1845-boot-guard-lifecycle.md)

Đã sửa trước khi commit:
- C1: lỗi ngoài dự kiến khi reconcile (COM, timeout schtasks…) làm bỏ qua bước mở khoá → bắt hết, luôn settle khoá. 3 test.
- C2: bản không phải single-file chép vào Program Files sẽ không chạy → từ chối (có `EVBlocker.Core.dll` cạnh exe).
- H1: khoá đã Released thì bỏ qua đăng ký lại task → prepare luôn chạy khi được yêu cầu.
- H2: tắt được task khi firewall Off nhưng khoá còn → chặn theo trạng thái khoá, sync trước khi gỡ task.
- M1: chờ firewall tính theo đồng hồ thật (không vượt giới hạn task). M3: UI bắt mọi exception. M4: so size + thời gian trước khi hash. L4: dọn `.new`.

Chưa sửa (ghi nhận): M2 BFE khởi động lại giữa phiên → khoá bật tới lần boot/mở app admin sau (hiếm); L1-L3.
Chưa chạy lại tích hợp elevated sau các sửa này (UAC bị huỷ) — chạy ở phase 4.