# EVBlocker — Hướng dẫn sử dụng

Chặn toàn bộ truy cập internet ra ngoài trên Windows 10/11, trừ những ứng dụng bạn cho phép.

## Cài đặt

Tải `EVBlocker.exe` từ [trang phát hành](https://github.com/ncthanhngo/EVBlocker/releases). Một
file duy nhất, **không cần cài .NET**.

File chưa được ký số nên SmartScreen sẽ cảnh báo ở lần chạy đầu: bấm **More info → Run anyway**.

## Quyền Admin

| Việc | Cần Admin? |
|---|---|
| Xem ứng dụng đang kết nối | Không |
| Xem **đường dẫn** của tiến trình | Có |
| Đọc lịch sử từ Security log | Có |
| Thêm/xoá app trong allow-list (chỉ lưu file) | Không |
| **Áp dụng** allow-list vào firewall | Có |
| Bật/tắt chặn outbound | Có |
| Bật tự áp lại khi khởi động | Có |

Ứng dụng mở ở quyền thường và có nút **Chạy lại với quyền Admin** khi cần. Nó không bắt bạn qua
UAC mỗi lần mở chỉ để xem danh sách kết nối.

## Quy trình khuyến nghị

Bật chặn outbound mà chưa biết máy cần gì là cách nhanh nhất để mất mạng. Làm theo thứ tự:

### 1. Quan sát trước

Bật audit policy để Windows ghi lại app nào cố ra internet. Chạy PowerShell **as Administrator**:

```powershell
auditpol /set /subcategory:"{0CCE9226-69AE-11D9-BED3-505054503030}" /failure:enable
```

Dùng GUID chứ không dùng tên, vì tên subcategory bị dịch theo ngôn ngữ Windows.

Chỉ bật `/failure` (event 5157 — bị chặn). Bật thêm `/success` sinh ra cực nhiều sự kiện và làm
đầy Security log rất nhanh.

Dùng máy vài ngày như bình thường, rồi xem tab **Đã thử kết nối**.

### 2. Dựng allow-list

Tab **Allow-list** có ba cách:

- **Quét ứng dụng đang chạy** — liệt kê mọi ứng dụng đang chạy trên máy, tích cái nào được ra
  internet. Cái đang có kết nối được tích sẵn và xếp lên đầu. Thành phần trong thư mục Windows
  bị ẩn mặc định, vì cho phép chúng theo đường dẫn hầu như luôn sai — baseline đã lo phần đó theo
  tên service.
- **Thêm ứng dụng phổ biến** — dò các ứng dụng quen thuộc (trình duyệt, IDE, công cụ đồng bộ,
  Zalo, Claude/Codex CLI) và chỉ thêm những cái **thật sự đã cài**. Máy nào cũng chỉ có một phần
  trong danh sách đó, nên nút này không thêm thứ máy không có.
- **Thêm ứng dụng…** — tự chọn file `.exe`.

**Không cần tạo rule chặn cho những app không tích.** Khi bật chặn outbound, mọi thứ không có
rule cho phép đều bị chặn. Một danh sách chặn tường minh còn yếu hơn: phần mềm cài sau lần quét
sẽ không nằm trong đó, và sẽ được cho qua.

Lưu ý khi quét không có quyền Admin: Windows chỉ cho đọc đường dẫn của khoảng một nửa số tiến
trình. Dialog hiện rõ tỉ lệ đọc được; muốn danh sách đầy đủ thì chạy lại với quyền Admin.

Nhiều ứng dụng có nhiều file thực thi (launcher, updater, tiến trình con). Tab **Đang kết nối**
cho biết file nào thật sự mở kết nối.

Muốn bổ sung ứng dụng vào danh mục dò thì tạo file ghi đè (xem bảng đường dẫn bên dưới) theo cấu
trúc của danh mục gốc; đường dẫn dùng được biến môi trường và một `*` cho mỗi đoạn — cần thiết
cho phần mềm cài vào thư mục đặt tên theo phiên bản.

Bấm **Áp dụng vào Firewall** để ghi rule. Chưa bật chặn thì rule này chưa có tác dụng gì.

### 3. Bật chặn

Ở thanh trên cùng: chọn thời gian tự khôi phục rồi bấm **Bật chặn outbound**.

Ứng dụng sẽ, theo đúng thứ tự:

1. Sao lưu toàn bộ cấu hình firewall hiện tại
2. Ghi baseline của Windows + allow-list của bạn
3. **Hẹn giờ tự khôi phục**
4. Rồi mới chặn

Chặn là bước cuối cùng, sau khi đường lui đã tồn tại.

### 4. Kiểm tra rồi xác nhận

Kiểm mạng còn chạy: duyệt web, Windows Update, và các ứng dụng bạn đã cho phép.

- **Ổn** → bấm **Giữ cấu hình**. Hẹn giờ khôi phục bị huỷ.
- **Không ổn** → bấm **Tắt chặn**, hoặc chỉ cần đợi. Máy tự khôi phục kể cả khi bạn tắt ứng
  dụng, kill tiến trình, hoặc khởi động lại máy.

## Tự khôi phục hoạt động thế nào

Nó là một **scheduled task chạy dưới SYSTEM**, không phải bộ đếm trong ứng dụng. Bộ đếm trong
ứng dụng sẽ chết cùng ứng dụng — đúng vào lúc cần nó nhất.

Task đặt `StartWhenAvailable`, nên nếu máy tắt ngang qua thời điểm hẹn thì lần khởi động sau nó
vẫn chạy. Nó cũng không chờ có mạng, vì chính cấu hình đang được khôi phục có thể là lý do máy
trông như mất mạng.

Nếu ứng dụng không phải bên hẹn giờ (ví dụ bạn đã đóng rồi mở lại), nó vẫn biết **có** hẹn giờ
đang chờ nhưng **không biết chính xác lúc nào** — và nó nói thẳng điều đó thay vì bịa ra một con
số đếm ngược.

## Tự áp lại khi khởi động

Bật ở thanh trên cùng. Nó đăng ký một task chạy lúc khởi động để kiểm tra rule có bị thay đổi
không, và áp lại nếu có.

**Việc chặn không cần cái này.** `DefaultOutboundAction` và rule nằm trong cấu hình Windows
Firewall, do service `MpsSvc` áp — service này chạy rất sớm trong quá trình khởi động, trước mọi
phần mềm. Chính sách có hiệu lực ngay cả khi bạn không bao giờ mở ứng dụng.

Cái này bắt trường hợp khác: installer, Group Policy, tool khác, hoặc Windows reset xoá mất rule.
Đang chặn mà mất rule baseline nghĩa là máy mất DNS và mất cập nhật, và không có gì khác đặt
chúng trở lại.

Nó **chỉ sửa rule**, không bao giờ tự bật lại việc chặn. Ứng dụng không lưu "ý định" ở đâu cả,
nên một profile đang không chặn thì không phân biệt được với profile bạn chủ động tắt — và đoán
mò điều đó lúc khởi động không phải việc của nó.

## Vị trí file

| Đường dẫn | Nội dung |
|---|---|
| `%ProgramData%\EVBlocker\allowlist.json` | Allow-list (toàn máy) |
| `%ProgramData%\EVBlocker\baseline-allow.json` | Ghi đè baseline, nếu bạn tạo |
| `%ProgramData%\EVBlocker\known-apps.json` | Ghi đè danh mục ứng dụng phổ biến, nếu bạn tạo |
| `%ProgramData%\EVBlocker\backups\*.wfw` | Bản sao lưu firewall, giữ 10 bản mới nhất |
| `%ProgramData%\EVBlocker\reconcile.log` | Nhật ký của lần chạy lúc khởi động |
| `%LOCALAPPDATA%\EVBlocker\settings.json` | Giao diện sáng/tối (riêng từng người dùng) |
| `%LOCALAPPDATA%\EVBlocker\crash.log` | Lỗi không xử lý được |

Rule trong Windows Firewall đều mang group `EVBlocker` — xem trong `wf.msc`, hoặc:

```powershell
Get-NetFirewallRule -Group EVBlocker
```

## Khôi phục bằng tay

Nếu mất mạng và không mở được ứng dụng, chạy PowerShell **as Administrator**:

```powershell
# Trả mặc định về cho phép
netsh advfirewall set allprofiles firewallpolicy allowinbound,allowoutbound

# Hoặc khôi phục toàn bộ từ bản sao lưu (ghi đè MỌI rule, kể cả của phần mềm khác)
netsh advfirewall import "C:\ProgramData\EVBlocker\backups\firewall-YYYYMMDD-HHMMSS.wfw"
```

## Baseline — những gì Windows luôn được phép

10 service, scope theo **tên service** chứ không theo đường dẫn file. Chúng dùng chung
`svchost.exe`, nên rule theo đường dẫn sẽ cho phép *mọi* service trong cùng tiến trình đó.

`Dnscache` · `Dhcp` · `NlaSvc` · `wuauserv` · `BITS` · `DoSvc` · `WinDefend` · `cryptsvc` ·
`W32Time` · `sppsvc`

Các rule **Core Networking** có sẵn của Windows vẫn giữ nguyên — chúng đã lo DHCP, DNS, IPv6 và
ICMP, viết lại là tự tạo một bản sao kém hơn.

Cần thêm service? Tạo `%ProgramData%\EVBlocker\baseline-allow.json` theo đúng cấu trúc của file
gốc; nó sẽ thay thế danh sách mặc định.

## Giới hạn — đọc trước khi tin tưởng

**Đây không phải hàng rào bảo mật.** Một tiến trình chạy quyền admin tự xoá được rule. Ứng dụng
này giúp kiểm soát phần mềm hoạt động bình thường, không chống được phần mềm cố tình phá.

**Cho phép công cụ chạy script = cho phép mọi thứ chạy qua nó.** `powershell.exe`, `cmd.exe`,
`curl.exe`, `python.exe` và tương tự — ứng dụng có cảnh báo trước khi thêm.

**Không lọc theo tên miền hay URL.** Windows Firewall không làm được việc đó. Chỉ chặn được theo
chương trình.

**Không chặn được chi tiết hơn tên service** với những thứ chạy trong `svchost.exe` hay
`rundll32.exe`.

**Ứng dụng Microsoft Store (UWP)** dùng package identity chứ không dùng đường dẫn exe — chưa hỗ
trợ.

**Có một khe hở rất sớm trong quá trình khởi động**, trước khi `MpsSvc` áp đầy đủ chính sách.
Đây là đặc tính của Windows, không kiểm soát được.

**UDP không thấy được đích ở tab Đang kết nối.** Windows không cung cấp địa chỉ đích trong bảng
UDP, kể cả với socket đã kết nối. Nghĩa là QUIC/HTTP3 và DNS không xác định được đích bằng cách
quét. Tab **Đã thử kết nối** thì có — event 5156/5157 *có* kèm địa chỉ đích cho UDP.

## Cập nhật

**Cài đặt → Kiểm tra bản mới.** Chỉ chạy khi bạn bấm.

Ứng dụng này tồn tại để ngăn phần mềm tự ý liên hệ internet. Một ứng dụng tự gọi server mỗi lần
mở để hỏi về chính nó là đang làm đúng thứ nó được cài để ngăn.

Nó báo và mở trang phát hành; việc tải và thay file do bạn làm thủ công.
