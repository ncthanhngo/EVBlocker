# SSH remote admin panel

Status: in progress

Thêm mục **SSH** vào app: một bảng cho admin quản lý và bấm-để-ssh vào các máy cùng mạng. Máy đích
tự bật OpenSSH Server khi chạy app với quyền admin.

## Quyết định của người dùng
- App tự tạo cặp khoá SSH trên máy quản trị (không dán tay).
- Máy đích **giữ đăng nhập mật khẩu Windows làm dự phòng** (không tắt) — tránh bị khoá ngoài nếu
  khoá cài lỗi.
- Cổng vào bảng SSH: mật khẩu, mặc định `3214` (chốt che mắt, không phải bảo mật thật).

## Phases
- phase-01: máy khách — bảng SSH, cổng mật khẩu, danh sách máy (tên + IP + user), sinh khoá, bấm
  biểu tượng terminal để `ssh`. Test được bằng ảnh chụp UI.
- phase-02: máy đích — `SshServerProvisioner`: bật sshd, mở cổng 22 cho LocalSubnet, cài khoá công
  khai, giữ mật khẩu. Tự chạy khi app chạy elevated. **Không test được ở đây (cần admin + máy thật).**

## Acceptance
- Mục SSH hỏi mật khẩu; đúng `3214` mới vào.
- Thêm/sửa/xoá máy; danh sách lưu lại giữa các lần mở.
- Bấm biểu tượng terminal mở cửa sổ `ssh -i <khoá> user@ip`.
- Bảng hiện khoá công khai để chép sang máy đích.
- Provisioner idempotent, giữ password auth, chỉ mở cổng cho LocalSubnet.

## Ràng buộc / rủi ro
- Khoá công khai phải tới được máy đích: hoặc chép file `ssh-admin.pub` cạnh exe khi triển khai
  (tự động khi chạy elevated), hoặc dán tay + bấm "Chuẩn bị máy này" trên máy đích. Máy đích không
  thể chấp nhận khoá nó chưa từng thấy — đây là bước bootstrap không tránh được.
- Wi-Fi công ty có thể bật cách ly máy khách → các máy không thấy nhau. Kiểm tra trước.
- Phần provisioning chạy elevated, chưa test trên máy thật trong phiên này.
