# Kiểm chứng khoá lúc khởi động — reboot thật

Máy: Windows 11 Pro 26200, đang bật chặn (Domain/Private/Public = Block), audit
"Filtering Platform Connection" = Failure. Bản trong Program Files lúc reboot: build 18:42
(trước các sửa theo review — đường mở khoá giống nhau).

## Dòng thời gian

| Thời điểm | Nguồn | Sự kiện |
|---|---|---|
| 19:17:43 | `Win32_OperatingSystem.LastBootUpTime` | Khởi động |
| 19:17:51.926 | Security 5157 | `System` → `ff02::16` bị chặn, **FilterOrigin = Unknown** — không phải rule hay default filter của firewall ⇒ filter WFP của EVBlocker |
| 19:17:54.18 | `reconcile.log` | `No drift. Enforcement is On. Boot guard: released.` |
| 19:17:56.353 → | Security 5157 | Mọi lần chặn có FilterOrigin = `Default Outbound` / `WSH Default` / `Query User Default` — firewall đã tiếp quản |
| 19:18:02 | `Win32_LogonSession` | Người dùng đăng nhập |

## Kết luận

- Khoá **chặn thật** trong khe hở trước khi `MpsSvc` áp policy (sự kiện 19:17:51, origin Unknown).
- Mở chốt **11 s sau khi boot, trước khi đăng nhập** ⇒ ứng dụng của người dùng không phải chờ.
- Không có khoảng trống giữa khoá và firewall: sự kiện đầu tiên có origin firewall là 19:17:56, sau
  lúc mở chốt 2 s; không có sự kiện chặn nào vắng mặt giữa hai nguồn (audit success không bật nên
  không chứng minh được "không lọt", chỉ chứng minh được "có chặn").
- 699 sự kiện 5157 trong 3 phút đầu; 1 từ khoá, phần còn lại từ firewall.

## Giới hạn của bằng chứng

- Event Log chỉ ghi được sau khi service EventLog chạy; các giây sớm hơn (tcpip → EventLog) không
  thấy được. Filter BOOTTIME có trong `netsh wfp show state` (phase 1) là bằng chứng cho giai đoạn đó.
- Audit chỉ bật Failure ⇒ không có 5156 để tìm kết nối lọt.

## Sau reboot

- Chạy lại bản đã cài bằng quyền admin: bản Program Files được cập nhật lên build 18:52 (có sửa C1,
  C2, H1, H2…). Run key sửa về `%LOCALAPPDATA%\Programs\EVBlocker\EVBlocker.exe --tray` — trước đó
  trỏ vào `artifacts\` do kiểm thử tích hợp chạy exe từ đó.

## Bật/tắt chặn qua giao diện thật (19:29)

UI Automation, elevated, trên cửa sổ thật:

| Bước | Khoá | Profile | Dead-man |
|---|---|---|---|
| Trước | 14 filter | Block ×3 | không |
| Ngừng chặn | **0** | Allow ×3 | không |
| Bắt đầu chặn → Giữ nguyên | **14** | Block ×3 | không (đã huỷ) |

Task khởi động vẫn trỏ `C:\Program Files\EVBlocker\EVBlocker.exe`.

Hạn chế của lần chạy: script không bấm được "Yes" của hộp xác nhận (tìm dialog ở gốc cây UIA,
dialog thuộc cửa sổ app) — việc bật vẫn diễn ra, nhiều khả năng người dùng bấm tay. Vì vậy không
đo được task dead-man lúc Armed có 2 action; phần đó có unit test + test đăng ký thật với Task
Scheduler.

## Không làm, có lý do

- Reboot sau khi tắt chặn: không có khoá thì không có gì EVBlocker can thiệp lúc boot — kết quả
  bằng Windows mặc định, không chứng minh thêm điều gì.
- Cố ý làm hỏng rồi khôi phục bằng script: script đã gỡ sạch khoá trên máy thật trong kiểm thử
  tích hợp; reboot chỉ thêm một lần mất mạng cho người dùng.
- Tự xử lý BFE khởi động lại giữa phiên: BFE không dừng được khi Windows chạy bình thường. Ghi
  vào mục Giới hạn của hướng dẫn, kèm cách khôi phục.
