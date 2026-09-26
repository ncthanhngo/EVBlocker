# Phase 4 — Kiểm chứng trên máy thật

Cần người dùng: có reboot, cần UAC.

## Steps
1. Bật audit 5157 (app đã có nút). Bật chặn, bấm Giữ nguyên.
2. `netsh wfp show state` ⇒ có khoá (BOOTTIME + PERSISTENT) + chốt mở.
3. Reboot. Sau khi đăng nhập:
   - `reconcile.log` có dòng "đã mở chốt" kèm thời gian chờ.
   - Security log 5157 có sự kiện chặn với Filter Origin là filter của EVBlocker, thời điểm trước
     lúc MpsSvc áp policy ⇒ khoá đã thực sự chặn trong khe hở.
   - App được phép (trình duyệt) ra mạng; app không được phép thì không.
4. Đo thêm bao nhiêu giây mất mạng lúc boot (thời điểm mở chốt so với thời điểm đăng nhập).
5. Tắt chặn ⇒ `netsh wfp show state` không còn filter EVBlocker. Reboot ⇒ mạng bình thường.
6. Thử đường hỏng: bật chặn, xoá task khởi động, reboot ⇒ mất mạng ⇒ chạy script khôi phục ⇒ có mạng.

## Report
`plans/reports/verification-<date>-boot-guard.md`: số liệu, log, ảnh chụp nếu cần.

## Kết quả (2026-09-26 19:18) — [report](../reports/verification-260926-1918-boot-guard.md)

- Bước 1-4 đạt: 5157 lúc 19:17:51 với FilterOrigin=Unknown (filter của khoá) trước khi mở chốt
  19:17:54; firewall tiếp quản từ 19:17:56; đăng nhập 19:18:02 ⇒ không phải chờ.
- Bước 5-6 chưa làm.
