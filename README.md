# EVBlocker

Chặn toàn bộ truy cập internet ra ngoài trên Windows 10/11, trừ những ứng dụng bạn cho phép.

Một file `.exe` duy nhất, không cần cài .NET.

## Làm được gì

- **Chặn mặc định, cho phép theo danh sách** — mọi ứng dụng không nằm trong allow-list đều không ra được internet
- **Giữ Windows chạy bình thường** — DNS, Windows Update, Defender, kích hoạt bản quyền, đồng bộ giờ
- **Xem ai đang kết nối** — tiến trình nào đang mở kết nối ra IP public, kèm đường dẫn
- **Xem ai đã từng thử** — đọc từ nhật ký Windows Filtering Platform
- **Tự khôi phục nếu hỏng** — bật chặn xong mà mất mạng, máy tự trả về như cũ

## Bắt đầu

Tải `EVBlocker.exe` từ [Releases](https://github.com/ncthanhngo/EVBlocker/releases).

Đọc [hướng dẫn sử dụng](docs/huong-dan-su-dung.md) trước khi bật chặn — **bật khi chưa biết máy
cần gì là cách nhanh nhất để mất mạng**. Quy trình khuyến nghị là quan sát vài ngày, dựng
allow-list, rồi mới bật.

## Tự khôi phục hoạt động thế nào

Khi bật chặn, ứng dụng làm theo đúng thứ tự này:

```
sao lưu cấu hình → ghi rule → HẸN GIỜ KHÔI PHỤC → rồi mới chặn
```

Chặn là bước cuối, sau khi đường lui đã tồn tại.

Hẹn giờ là một **scheduled task chạy dưới SYSTEM**, không phải bộ đếm trong ứng dụng. Nó vẫn chạy
kể cả khi bạn tắt ứng dụng, kill tiến trình, hoặc khởi động lại máy — đúng vào lúc một bộ đếm
trong ứng dụng đã chết cùng ứng dụng.

Không bấm **Giữ cấu hình** trong thời gian đã chọn thì máy tự trở lại như cũ.

## Đây không phải công cụ bảo mật

Một tiến trình chạy quyền admin tự xoá được rule. EVBlocker giúp bạn kiểm soát phần mềm hoạt động
bình thường — nó không chống được phần mềm cố tình phá.

Vài giới hạn khác, đều ghi rõ trong [hướng dẫn](docs/huong-dan-su-dung.md#giới-hạn--đọc-trước-khi-tin-tưởng):
không lọc theo tên miền, không tách được các service chung `svchost.exe` chi tiết hơn tên service,
chưa hỗ trợ ứng dụng Microsoft Store, và cho phép `powershell.exe` nghĩa là cho phép mọi thứ chạy
qua nó.

## Tài liệu

| Tài liệu | Cho ai |
|---|---|
| [Hướng dẫn sử dụng](docs/huong-dan-su-dung.md) | Người dùng |
| [Kiến trúc](docs/system-architecture.md) | Người bảo trì |
| [Plan](plans/260925-0730-evblocker-outbound-firewall/plan.md) | Ghi chép quá trình dựng dự án |

## Dựng từ mã nguồn

Cần .NET 8 SDK.

```powershell
dotnet test                                                       # 353 test, không cần admin
dotnet publish src/EVBlocker.App -p:PublishProfile=SelfContained  # 1 file, ~63 MB
```

Các thao tác cần quyền admin không unit test được. Kiểm chúng bằng công cụ riêng, chạy trong
PowerShell **as Administrator**:

```powershell
.\tools\EVBlocker.Verify\bin\Debug\net8.0-windows\EVBlocker.Verify.exe
```

Nó tạo rồi xoá một rule, export cấu hình, đăng ký rồi huỷ một task dưới SYSTEM — **không bật
chặn** trong tường lửa, và đọc `DefaultOutboundAction` ở đầu và cuối để chắc chắn không có gì làm
nó thay đổi. Nó cũng cài, bật thử rồi gỡ khoá lúc khởi động — **máy mất mạng vài giây** ở bước
này. Chạy qua `dotnet EVBlocker.Verify.dll` (khi `dotnet.exe` nằm trong danh sách cho phép) thì
bước đó kiểm được cả việc chặn thật, không chỉ trạng thái.

## Lưu ý

Tệp chưa được ký số nên SmartScreen sẽ cảnh báo ở lần chạy đầu: **More info → Run anyway**.

## Giấy phép

Chưa chọn giấy phép.
