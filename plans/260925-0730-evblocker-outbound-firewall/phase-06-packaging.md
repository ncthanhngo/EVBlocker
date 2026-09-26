# Phase 06 — Đóng gói

**Status:** xong. Bản publish đã chạy thử thật, cả UI lẫn chế độ `--reconcile`.
**Chạm firewall:** không

## Kết quả

| Hạng mục | Thực tế |
|---|---|
| Dung lượng | **62.9 MB**, đúng **1 file** (ngưỡng 80 MB) |
| Lệnh | `dotnet publish src/EVBlocker.App -p:PublishProfile=SelfContained` |
| Chạy trên máy không có .NET | self-contained nên được, **chưa test trên VM trắng** |

## Ba điểm lệch so với plan

**Trimming: không dùng được.** Plan ghi `TrimMode=partial`. Thực tế SDK từ chối thẳng:
`NETSDK1168: WPF is not supported or recommended with trimming enabled`. Nên 62.9 MB là **sàn**
cho một bản chạy được trên máy trắng, không phải con số có thể tối ưu thêm bằng trim.

**`InvariantGlobalization`: bỏ.** Plan liệt kê nó như cách tiết kiệm dung lượng. Nhưng UI tiếng
Việt và code format ngày, sắp tên qua `CurrentCulture` — bật nó là ngày hiện theo thứ tự Mỹ và
tên tiếng Việt sắp sai. Không đáng đổi vài MB lấy giao diện trông như lỗi.

**Cấu hình publish phải nằm trong `.pubxml`, không phải trong csproj.** Tôi đặt
`RuntimeIdentifier`/`SelfContained` vào `<PropertyGroup>` của project kèm comment "chỉ áp dụng khi
publish" — **sai**. Chúng đổi luôn đường ra của build thường sang
`bin/Debug/net8.0-windows/win-x64/`, để lại một exe cũ ở đường cũ. Tôi chụp nhầm exe cũ đó và
tưởng code không có tác dụng. Chỉ lộ ra khi đối chiếu timestamp của source với exe.

## Còn lại

- **Chưa ký số.** SmartScreen sẽ cảnh báo ở lần chạy đầu trên máy lạ. Ghi rõ trong release notes.
- **Chưa test trên VM Windows trắng** (không cài .NET) — điều kiện nghiệm thu duy nhất của phase
  này chưa kiểm được.

## Phát hành

`.github/workflows/release.yml` chạy khi push tag `v*`: test → publish → kiểm dung lượng → tạo
release kèm file exe. Kiểm dung lượng có cả cận trên và cận dưới, vì một artifact đột nhiên nhỏ đi
nghĩa là cấu hình publish đã đổi và file có thể không còn chạy được trên máy không có .NET.

Tag phải parse được thành version (`v1.2.3`), vì đó chính là thứ tính năng kiểm tra cập nhật
trong app đem ra so sánh.
