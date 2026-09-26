# Phase 3 — Khôi phục + tài liệu

## Requirements
- `EVBlocker.exe --remove-boot-guard` (admin): gỡ khoá, in kết quả, exit code 0/1.
- Script độc lập `tools/remove-boot-guard.ps1`: P/Invoke `fwpuclnt.dll` qua `Add-Type`, xoá filter
  theo GUID cố định rồi sublayer, provider. Chạy được khi không còn `EVBlocker.exe`. Khi Enable,
  copy script vào `%ProgramFiles%\EVBlocker\`.
- Tài liệu:
  - `docs/huong-dan-su-dung.md`: sửa mục "Giới hạn" (~dòng 304) — khe hở đã đóng khi bật chặn;
    thêm mục "Mất mạng sau khi khởi động lại" với 2 lệnh khôi phục; ghi rõ vài giây mất mạng lúc boot.
  - `docs/system-architecture.md`: khoá + chốt mở, vì sao chốt mở không persistent, kết quả đo WFP.
  - Sửa câu sai trong `plans/260925-0730-evblocker-outbound-firewall/plan.md` ("MpsSvc … trước mọi
    phần mềm user-mode").

## Validation
- Chạy script trên máy đang có khoá (Verify tool dựng khoá) ⇒ sạch.
- Đọc lại tài liệu: lệnh copy-paste chạy được, đường dẫn đúng.

## Kết quả (2026-09-26)

Xong: `--remove-boot-guard`, `tools/remove-boot-guard.ps1` (nhúng vào exe, chép ra Program Files,
test đối chiếu GUID), hướng dẫn sử dụng (mục Khoá lúc khởi động, Mất mạng sau khi khởi động lại,
Giới hạn, Vị trí file), kiến trúc (mục boot guard + bẫy `show filters`), sửa câu sai ở plan cũ.
Script đặt console UTF-8 — lần chạy đầu in tiếng Việt thành `?`.
