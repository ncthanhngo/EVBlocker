# EVBlocker — Hướng dẫn sử dụng

Chặn toàn bộ truy cập internet ra ngoài trên Windows 10/11, trừ những ứng dụng bạn cho phép.

## Cài đặt

Tải `EVBlocker.exe` từ [trang phát hành](https://github.com/ncthanhngo/EVBlocker/releases). Một
file duy nhất, **không cần cài .NET**.

File chưa được ký số nên SmartScreen sẽ cảnh báo ở lần chạy đầu: bấm **More info → Run anyway**.

## Quyền quản trị

| Việc | Cần quyền quản trị? |
|---|---|
| Xem ứng dụng đang kết nối | Không |
| Xem **đường dẫn** của tiến trình | Có |
| Đọc lịch sử từ nhật ký Windows | Có |
| Thêm/bỏ phần mềm trong danh sách cho phép (chỉ lưu file) | Không |
| **Lưu** danh sách cho phép vào tường lửa | Có |
| Bắt đầu chặn / ngừng chặn | Có |
| Bật tự kiểm tra lại khi mở máy | Có |

Ứng dụng mở ở quyền thường và có nút **Chạy lại với quyền quản trị** khi cần. Nó không bắt bạn qua
UAC mỗi lần mở chỉ để xem danh sách kết nối.

## Quy trình khuyến nghị

Bắt đầu chặn khi chưa biết máy cần gì là cách nhanh nhất để mất mạng. Làm theo thứ tự:

### 1. Quan sát trước

Bật audit policy để Windows ghi lại app nào cố ra internet. Chạy PowerShell **as Administrator**:

```powershell
auditpol /set /subcategory:"{0CCE9226-69AE-11D9-BED3-505054503030}" /failure:enable
```

Dùng GUID chứ không dùng tên, vì tên subcategory bị dịch theo ngôn ngữ Windows.

Chỉ bật `/failure` (event 5157 — bị chặn). Bật thêm `/success` sinh ra cực nhiều sự kiện và làm
đầy Security log rất nhanh.

Dùng máy vài ngày như bình thường, rồi xem tab **Đã thử kết nối**.

### 2. Dựng danh sách cho phép

Một số ứng dụng **tự có sẵn trong danh sách cho phép** mỗi lần mở ứng dụng, không cần bấm gì. Danh mục
hướng vào máy trạm lập trình:

| Nhóm | Ứng dụng |
|---|---|
| Mã nguồn | Git · Git HTTPS · Git SSH · OpenSSH · GitHub CLI |
| Runtime, trình quản lý gói | Node.js · Python · .NET SDK · Go · Rust (cargo, rustup) · Java |
| IDE, editor | VS Code · Visual Studio · GoLand · IntelliJ IDEA · PyCharm · WebStorm · CLion · Rider · DataGrip · JetBrains Toolbox · Cursor · Windsurf |
| AI CLI | Claude CLI · Claude Desktop · Codex CLI |
| Container | Docker Desktop |
| API, thiết kế, ghi chú | Postman · Figma · Notion |
| Trình duyệt, đồng bộ, liên lạc | Chrome · Edge · OneDrive · Google Drive · Zalo · Slack · Teams (bản cũ) · Discord |

**Teams bản mới không cho phép theo đường dẫn được** — nó là ứng dụng Microsoft Store, giống
trường hợp `winget` nói ở phần giới hạn. Danh mục chỉ dò được Teams bản cũ.

**Git cần ba file, không phải một.** `cmd\git.exe` chỉ là shim 47 KB; client thật nằm ở
`mingw64\bin\git.exe`, còn việc tải/đẩy qua HTTPS do một file riêng `git-remote-https.exe` làm.
Chỉ cho phép file trên PATH thì `git clone https://...` vẫn bị chặn trong khi danh sách cho phép trông
như đã đúng.

Chỉ những cái **thật sự đã cài trên máy** mới được thêm. Không thấy một cái trong danh sách nghĩa
là máy chưa cài nó — tạo rule trỏ vào file không tồn tại thì Windows vẫn nhận, và danh sách cho phép trông
như đúng trong khi không cho phép gì cả.

Gỡ một ứng dụng khỏi danh sách là **quyết định vĩnh viễn**: nó được ghi lại trong `allowlist.json`
nên lần mở sau không bị thêm lại. Muốn lấy lại thì bấm **Thêm ứng dụng phổ biến**.

### Mục "Phần mềm đã cài"

Liệt kê đúng những gì Control Panel → Programs hiển thị, đọc từ cùng ba nhánh registry, nên phần
mềm vừa cài xong đã có mặt ngay lần mở kế tiếp — không có gì phải làm mới hay đồng bộ.

Tích phần mềm nào thì file chạy của nó vào danh sách cho phép. Bộ lọc **Chỉ hiện mục chưa quyết định**
là chỗ phần mềm mới cài rơi vào. **Ẩn driver và thành phần nền** bật sẵn vì driver, bộ cài đi kèm và
thành phần nền chiếm phần lớn danh sách và không tự ra internet; số mục đang ẩn luôn hiện ở
thanh dưới. Thành phần của Windows không cần quyết định gì — ứng dụng đã tự cho phép các dịch vụ hệ
thống cần thiết.

**Một mục gỡ cài đặt mô tả *phần mềm*, còn firewall cần *file .exe*, và registry không phải lúc
nào cũng bắc được cầu đó.** Đo trên một máy thật: 35 phần mềm, 16 xác định được file, 19 không.
Dòng không xác định được vẫn hiện nhưng không tích được — thêm bằng **Thêm ứng dụng…**.

Hai chỗ dễ sai đã được xử lý, nhưng nên biết:

- `DisplayIcon` rất hay trỏ vào **file cài đặt** chứ không phải phần mềm — đo được
  `OneDriveSetup.exe` cho OneDrive và `python-3.14.6-amd64.exe` trong Package Cache cho Python.
  Cho phép những file đó tạo rule cho thứ chạy một lần rồi thôi. Ứng dụng loại chúng ra.
- Thư mục cài đặt thường chứa nhiều file. Ứng dụng ưu tiên file **trùng tên phần mềm**, nên
  "Microsoft OneDrive" ra đúng `OneDrive.exe` thay vì mười file phụ trợ bên cạnh.

Git vẫn là ngoại lệ: mục Git ở đây chỉ ra các launcher trong thư mục gốc, không ra
`git-remote-https.exe`. Dùng danh mục mặc định cho Git, đừng dựa vào màn hình này.

Thêm thủ công, mục **Danh sách cho phép** có ba cách:

- **Quét ứng dụng đang chạy** — liệt kê mọi ứng dụng đang chạy trên máy, tích cái nào được ra
  internet. Cái đang có kết nối được tích sẵn và xếp lên đầu. Thành phần trong thư mục Windows
  bị ẩn mặc định, vì cho phép chúng theo đường dẫn hầu như luôn sai — baseline đã lo phần đó theo
  tên service.
- **Thêm ứng dụng phổ biến** — dò lại danh mục trên. Dùng khi vừa cài thêm một ứng dụng trong đó,
  hoặc muốn lấy lại cái đã gỡ.
- **Thêm ứng dụng…** — tự chọn file `.exe`.

**Không cần tạo danh sách chặn riêng.** Khi bạn bắt đầu chặn, mọi thứ không nằm trong danh sách
cho phép đều bị chặn. Một danh sách chặn tường minh còn yếu hơn: phần mềm cài sau lần quét sẽ
không nằm trong đó, và sẽ được cho qua.

Lưu ý khi quét không có quyền quản trị: Windows chỉ cho đọc đường dẫn của khoảng một nửa số tiến
trình. Dialog hiện rõ tỉ lệ đọc được; muốn danh sách đầy đủ thì chạy lại với quyền quản trị.

Nhiều ứng dụng có nhiều file thực thi (launcher, updater, tiến trình con). Mục **Đang kết nối**
cho biết file nào thật sự mở kết nối.

Muốn bổ sung ứng dụng vào danh mục dò thì tạo file ghi đè (xem bảng đường dẫn bên dưới) theo cấu
trúc của danh mục gốc; đường dẫn dùng được biến môi trường và một `*` cho mỗi đoạn — cần thiết
cho phần mềm cài vào thư mục đặt tên theo phiên bản.

Bấm **Lưu vào tường lửa** để ghi danh sách. Chưa bắt đầu chặn thì việc này chưa có tác dụng gì.

### 3. Bắt đầu chặn

Ở thanh trên cùng: chọn thời gian tự bỏ chặn rồi bấm **Bắt đầu chặn**.

Ứng dụng sẽ, theo đúng thứ tự:

1. Sao lưu toàn bộ cấu hình firewall hiện tại
2. Ghi các dịch vụ hệ thống Windows cần + danh sách cho phép của bạn
3. **Hẹn giờ tự bỏ chặn**
4. Rồi mới chặn

Chặn là bước cuối cùng, sau khi đường lui đã tồn tại.

### 4. Kiểm tra rồi xác nhận

Kiểm mạng còn chạy: duyệt web, Windows Update, và các ứng dụng bạn đã cho phép.

- **Ổn** → bấm **Giữ nguyên**. Hẹn giờ tự bỏ chặn bị huỷ.
- **Không ổn** → bấm **Ngừng chặn**, hoặc chỉ cần đợi. Máy tự bỏ chặn kể cả khi bạn tắt ứng
  dụng, kill tiến trình, hoặc khởi động lại máy.

## Tự bỏ chặn hoạt động thế nào

Nó là một **scheduled task chạy dưới SYSTEM**, không phải bộ đếm trong ứng dụng. Bộ đếm trong
ứng dụng sẽ chết cùng ứng dụng — đúng vào lúc cần nó nhất.

Task đặt `StartWhenAvailable`, nên nếu máy tắt ngang qua thời điểm hẹn thì lần khởi động sau nó
vẫn chạy. Nó cũng không chờ có mạng, vì chính cấu hình đang được khôi phục có thể là lý do máy
trông như mất mạng.

Nếu ứng dụng không phải bên hẹn giờ (ví dụ bạn đã đóng rồi mở lại), nó vẫn biết **có** hẹn giờ
đang chờ nhưng **không biết chính xác lúc nào** — và nó nói thẳng điều đó thay vì bịa ra một con
số đếm ngược.

## Tự kiểm tra lại khi mở máy

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
| `%ProgramData%\EVBlocker\allowlist.json` | Danh sách cho phép (dùng chung cho cả máy) |
| `%ProgramData%\EVBlocker\baseline-allow.json` | Ghi đè danh sách dịch vụ hệ thống, nếu bạn tạo |
| `%ProgramData%\EVBlocker\known-apps.json` | Ghi đè danh mục phần mềm quen thuộc, nếu bạn tạo |
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

**`node.exe` và `python.exe` nằm trong danh mục mặc định, và chúng chạy được mã tuỳ ý.** Máy trạm
lập trình không làm việc được nếu thiếu chúng — `npm install`, `pip install`, và phần lớn CLI đều
chạy qua chúng. Đây là đánh đổi có chủ ý, không phải sơ suất: ứng dụng nói rõ trên thanh trạng
thái mỗi khi tự thêm một công cụ loại này.

**`powershell.exe`, `pwsh.exe`, `cmd.exe`, `curl.exe` cố ý KHÔNG nằm trong mặc định.** Chúng cũng
chạy được mã tuỳ ý nhưng việc ra internet của chúng là tuỳ chọn (`Install-Module`,
`Invoke-WebRequest`), khác với runtime mà toolchain bắt buộc phải có. Cần thì thêm tay qua **Thêm
ứng dụng…**, ứng dụng sẽ hỏi lại trước khi thêm.

**`winget` không cho phép theo đường dẫn được.** File `WindowsApps\winget.exe` là alias thực thi,
**0 byte** — rule trỏ vào đó không khớp tiến trình thật, vốn nằm trong `Program Files\WindowsApps`
với đường dẫn đổi theo mỗi bản cập nhật. Tương tự với các ứng dụng Microsoft Store khác.

**Tiến trình chạy trong WSL2 không chặn được theo cách này.** Chúng là tiến trình Linux trong một
máy ảo, không phải tiến trình Windows, nên rule theo đường dẫn exe không áp dụng cho chúng.

**CLI viết bằng Node không cho phép riêng được.** Gemini CLI chẳng hạn, gói npm của nó khai báo
`bin: gemini.js` — không có file `.exe` nào, tiến trình thật sự mở kết nối là `node.exe`. Cho
phép `node.exe` là cho phép **mọi** script Node trên máy, nên nó rơi vào đúng cảnh báo ở trên và
không nằm trong danh mục mặc định. Claude CLI và Codex CLI thì khác: cả hai đều có binary thật
(`claude.exe`, `codex*.exe`), nên cho phép được chính xác.

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
quét. Mục **Đã thử kết nối** thì có — event 5156/5157 *có* kèm địa chỉ đích cho UDP.

## Cập nhật

**Cài đặt → Kiểm tra bản mới.** Chỉ chạy khi bạn bấm.

Ứng dụng này tồn tại để ngăn phần mềm tự ý liên hệ internet. Một ứng dụng tự gọi server mỗi lần
mở để hỏi về chính nó là đang làm đúng thứ nó được cài để ngăn.

Nó báo và mở trang phát hành; việc tải và thay file do bạn làm thủ công.
