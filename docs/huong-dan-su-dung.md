# EVBlocker — Hướng dẫn sử dụng

Chặn toàn bộ truy cập internet ra ngoài trên Windows 10/11, trừ những ứng dụng bạn cho phép.

## Cài đặt

Tải `EVBlocker.exe` từ [trang phát hành](https://github.com/ncthanhngo/EVBlocker/releases). Một
file duy nhất, **không cần cài .NET**.

File chưa được ký số nên SmartScreen sẽ cảnh báo ở lần chạy đầu: bấm **More info → Run anyway**.

## Chạy nền ở khay hệ thống

Nút đóng cửa sổ (và Alt+F4) **không tắt** ứng dụng mà ẩn nó xuống khay hệ thống, góc phải thanh
tác vụ. Bấm vào biểu tượng để mở lại; muốn tắt hẳn, bấm phải → **Thoát EVBlocker**. Mở lại
shortcut khi ứng dụng đang chạy chỉ đưa cửa sổ cũ lên, không mở thêm bản thứ hai.

Mặc định ứng dụng **tự chạy khi đăng nhập Windows**, ẩn ở khay. Tắt ở **Cài đặt → Chạy nền**.
Ứng dụng ghi mục `EVBlocker` vào `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (thấy trong
tab Startup của Task Manager), trỏ vào đúng file exe đang chạy — chuyển file sang chỗ khác rồi mở
lại một lần là mục này tự cập nhật.

Việc chặn **không phụ thuộc** vào ứng dụng có đang chạy hay không: nó nằm trong cấu hình Windows
Firewall. Chạy nền chỉ để bạn xem và chỉnh nhanh.

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

Mở **Theo dõi → Đã bị chặn** và bấm **Bật ghi nhật ký**. Cần quyền quản trị.

Windows không ghi lại việc phần mềm cố ra internet cho tới khi được bật, nên trước đó mục này
trống dù máy chạy bao lâu. Ứng dụng chỉ bật phần ghi **lần bị chặn**; ghi thêm cả lần thành công
sinh ra một sự kiện cho mỗi kết nối của máy và làm đầy nhật ký trong vài giờ.

Chỉ ghi **từ lúc bật trở đi** — không có dữ liệu của quá khứ.

Muốn làm bằng tay thì chạy PowerShell **as Administrator**:

```powershell
auditpol /set /subcategory:"{0CCE9226-69AE-11D9-BED3-505054503030}" /failure:enable
```

Dùng GUID chứ không dùng tên, vì tên subcategory bị dịch theo ngôn ngữ Windows.

Dùng máy vài ngày như bình thường, rồi quay lại đó và bấm **Tải lại**.

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
nên lần mở sau không bị thêm lại. Muốn lấy lại thì dùng **Thêm phần mềm… → Phần mềm quen thuộc đã cài**.

Mục **Phần mềm được phép** → nút **Thêm phần mềm…**, chọn một trong bốn:

- **Từ phần mềm đang chạy** — liệt kê mọi phần mềm đang chạy trên máy, tích cái nào được ra
  internet. Cái đang có kết nối được tích sẵn và xếp lên đầu. Thành phần của Windows bị ẩn mặc
  định, vì cho phép chúng theo đường dẫn hầu như luôn sai — ứng dụng đã tự lo phần đó theo tên
  dịch vụ.
- **Từ phần mềm đã cài…** — danh sách giống Control Panel, xem mục dưới.
- **Phần mềm quen thuộc đã cài** — dò lại danh mục trên. Dùng khi vừa cài thêm một phần mềm
  trong đó, hoặc muốn lấy lại cái đã gỡ.
- **Chọn file trên máy…** — tự chọn file `.exe`.

### Chọn từ phần mềm đã cài

**Thêm phần mềm… → Từ phần mềm đã cài…** mở đúng danh sách Control Panel → Programs hiển thị,
đọc từ cùng ba nhánh registry, nên phần mềm vừa cài xong đã có mặt ngay lần mở kế tiếp — không
có gì phải làm mới hay đồng bộ.

Tích phần mềm nào thì file chạy của nó vào danh sách cho phép. Bộ lọc **Chỉ hiện mục chưa quyết định**
là chỗ phần mềm mới cài rơi vào. **Ẩn driver và thành phần nền** bật sẵn vì driver, bộ cài đi kèm và
thành phần nền chiếm phần lớn danh sách và không tự ra internet; số mục đang ẩn luôn hiện ở
thanh dưới. Thành phần của Windows không cần quyết định gì — ứng dụng đã tự cho phép các dịch vụ hệ
thống cần thiết.

**Một mục gỡ cài đặt mô tả *phần mềm*, còn firewall cần *file .exe*, và registry không phải lúc
nào cũng bắc được cầu đó.** Đo trên một máy thật: 35 phần mềm, 16 xác định được file, 19 không.
Dòng không xác định được vẫn hiện nhưng không có ô tích — thêm bằng **Chọn file trên máy…**.

Hai chỗ dễ sai đã được xử lý, nhưng nên biết:

- `DisplayIcon` rất hay trỏ vào **file cài đặt** chứ không phải phần mềm — đo được
  `OneDriveSetup.exe` cho OneDrive và `python-3.14.6-amd64.exe` trong Package Cache cho Python.
  Cho phép những file đó tạo rule cho thứ chạy một lần rồi thôi. Ứng dụng loại chúng ra.
- Thư mục cài đặt thường chứa nhiều file. Ứng dụng ưu tiên file **trùng tên phần mềm**, nên
  "Microsoft OneDrive" ra đúng `OneDrive.exe` thay vì mười file phụ trợ bên cạnh.

Git vẫn là ngoại lệ: mục Git ở đây chỉ ra các launcher trong thư mục gốc, không ra
`git-remote-https.exe`. Dùng danh mục mặc định cho Git, đừng dựa vào màn hình này.

**Không cần tạo danh sách chặn riêng.** Khi bạn bắt đầu chặn, mọi thứ không nằm trong danh sách
cho phép đều bị chặn. Một danh sách chặn tường minh còn yếu hơn: phần mềm cài sau lần quét sẽ
không nằm trong đó, và sẽ được cho qua.

Lưu ý khi quét không có quyền quản trị: Windows chỉ cho đọc đường dẫn của khoảng một nửa số tiến
trình. Dialog hiện rõ tỉ lệ đọc được; muốn danh sách đầy đủ thì chạy lại với quyền quản trị.

Nhiều ứng dụng có nhiều file chạy (launcher, updater, tiến trình con). **Theo dõi → Đang kết
nối** cho biết file nào thật sự mở kết nối.

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

Ở mục **Cài đặt** dưới đáy thanh bên. Nó đăng ký một task chạy dưới SYSTEM lúc khởi động, trỏ vào
bản cài cố định `C:\Program Files\EVBlocker\EVBlocker.exe`. Task làm hai việc:

- **Mở khoá lúc khởi động** (xem mục dưới). Vì vậy khi đang chặn, task này **bắt buộc** — bắt đầu
  chặn sẽ tự bật nó, và nút tắt bị khoá tới khi ngừng chặn.
- **Đặt lại rule bị mất**: installer, Group Policy, tool khác, hoặc Windows reset xoá mất rule.
  Đang chặn mà mất rule baseline nghĩa là máy mất DNS và mất cập nhật, và không có gì khác đặt
  chúng trở lại.

Nó **chỉ sửa rule**, không bao giờ tự bật lại việc chặn. Ứng dụng không lưu "ý định" ở đâu cả,
nên một profile đang không chặn thì không phân biệt được với profile bạn chủ động tắt — và đoán
mò điều đó lúc khởi động không phải việc của nó.

## Khoá lúc khởi động

Cấu hình tường lửa **không** được Windows giữ sẵn qua lần khởi động: mỗi lần mở máy, service
`MpsSvc` áp lại từ đầu. Trước lúc đó Windows **cho mọi kết nối đi ra** — bộ chặn mặc định lúc boot
của Windows chỉ chặn chiều vào. Đo trên Windows 11 bằng `netsh wfp show state`. Service và driver
khởi động sớm lọt ra được trong vài giây đó.

Khi bạn bắt đầu chặn, EVBlocker cài thêm một bộ lọc cấp thấp (WFP) chặn **mọi** kết nối đi ra ngay
từ lúc mạng lên, trừ localhost và DHCP. Khi máy đang chạy, một bộ lọc thứ hai ("chốt mở") vô hiệu
nó — nên bình thường bạn không thấy gì khác. Chốt mở không sống qua lần khởi động; task tự kiểm
tra chờ tới khi tường lửa áp xong chính sách rồi mới đặt lại chốt mở.

Đánh đổi, cần biết:

- Mỗi lần mở máy, **cả phần mềm được phép** cũng chờ thêm vài giây mới có mạng.
- Tường lửa không sẵn sàng sau 3 phút thì task **vẫn mở chốt** và ghi rõ vào
  `reconcile.log` — mất mạng hẳn tệ hơn vài giây hở.
- Task hỏng hoặc bị xoá thì máy **không có mạng** sau khi khởi động lại. Xem mục
  [Mất mạng sau khi khởi động lại](#mất-mạng-sau-khi-khởi-động-lại).

Ngừng chặn hoặc tự bỏ chặn (hết giờ mà không bấm Giữ nguyên) đều gỡ khoá.

## Quét USB

Mục **USB** tự quét mỗi khi bạn cắm ổ — không cần bấm gì, không cần quyền quản trị để quét.

Nó tìm họ virus lây qua USB kiểu shortcut: virus đặt thư mục của bạn thành ẩn, rồi để lại một
shortcut **trùng tên** thư mục đó. Bấm vào shortcut thì payload chạy trước, sau đó mới mở thư mục
thật — nên nhìn bề ngoài mọi thứ vẫn bình thường.

| Mức | Nghĩa là gì | Bấm "Dọn ổ USB" sẽ làm gì |
|---|---|---|
| Chắc chắn là virus | Shortcut trùng tên thư mục đang bị ẩn, hoặc shortcut chạy `cmd`/`wscript` kèm tham số | Chuyển vào khu cách ly |
| Đáng ngờ | File chạy được nhưng bị đặt ẩn, hoặc có `autorun.inf` | Chuyển vào khu cách ly |
| Chỉ bị ẩn | Thư mục bị đặt ẩn — thường là thư mục của chính bạn | Cho hiện lại, **không** động vào |

**File bị cách ly không bị xoá.** Chúng được *chuyển* sang
`%ProgramData%\EVBlocker\quarantine\<thời điểm>\` kèm file `manifest.json` ghi đường dẫn gốc và
lý do, nên lấy lại được nếu nhận nhầm. Các quy tắc ở đây là suy đoán, và một lần nhầm mà xoá
thẳng là mất dữ liệu không lấy lại được.

Phần "cho hiện lại thư mục" chính là việc file `a.bat` vẫn làm bằng tay, nhưng tự động và không
phải tự nhận ra thư mục nào là của mình — `System Volume Information`, `$RECYCLE.BIN`, `RECYCLER`,
`EFI` và các thư mục macOS để lại đều được bỏ qua, nếu không thì ổ nào cũng bị báo nhiễm.

**Đây không phải phần mềm diệt virus.** Nó xử lý phần nằm trên USB. Nếu payload đã chạy trên máy
rồi thì việc dọn máy là của Windows Defender hoặc công cụ chuyên dụng — ứng dụng này không quét
bộ nhớ, không đọc registry, không gỡ thứ đã cài vào máy. Phần chặn mạng giúp nửa còn lại: họ worm
này đều gọi về máy chủ điều khiển, nên khi đang chặn thì payload có chạy cũng không ra được
internet.

Lưu ý: ổ cứng ngoài cắm qua USB thường được Windows báo là *ổ cứng* chứ không phải *ổ tháo rời*,
nên ứng dụng hỏi thẳng driver xem ổ nằm trên bus nào. Nhờ vậy ổ cứng USB — loại hay mang virus
nhất — không bị bỏ sót.

## Vị trí file

| Đường dẫn | Nội dung |
|---|---|
| `%ProgramData%\EVBlocker\allowlist.json` | Danh sách cho phép (dùng chung cho cả máy) |
| `%ProgramData%\EVBlocker\baseline-allow.json` | Ghi đè danh sách dịch vụ hệ thống, nếu bạn tạo |
| `%ProgramData%\EVBlocker\known-apps.json` | Ghi đè danh mục phần mềm quen thuộc, nếu bạn tạo |
| `%ProgramData%\EVBlocker\backups\*.wfw` | Bản sao lưu firewall, giữ 10 bản mới nhất |
| `%ProgramData%\EVBlocker\reconcile.log` | Nhật ký của lần chạy lúc khởi động, kể cả việc mở khoá |
| `C:\Program Files\EVBlocker\EVBlocker.exe` | Bản cài cố định mà task SYSTEM chạy; chép vào khi bắt đầu chặn |
| `C:\Program Files\EVBlocker\remove-boot-guard.ps1` | Script gỡ khoá lúc khởi động, dùng được khi không còn exe |
| `%ProgramData%\EVBlocker\quarantine\` | File cách ly từ USB, kèm manifest ghi nơi lấy ra |
| `%LOCALAPPDATA%\EVBlocker\settings.json` | Giao diện sáng/tối, tự chạy khi đăng nhập (riêng từng người dùng) |
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

# Gỡ khoá lúc khởi động (hai lệnh trên KHÔNG gỡ nó)
& "C:\Program Files\EVBlocker\EVBlocker.exe" --remove-boot-guard
```

### Mất mạng sau khi khởi động lại

Dấu hiệu khoá lúc khởi động chưa được mở: vừa khởi động lại, **không phần mềm nào** ra được
internet, kể cả phần mềm trong danh sách cho phép, và vẫn thế sau vài phút.

1. Xem `C:\ProgramData\EVBlocker\reconcile.log` — dòng cuối nói task đã chạy chưa và vì sao.
2. Gỡ khoá, trong PowerShell **as Administrator**:

   ```powershell
   & "C:\Program Files\EVBlocker\EVBlocker.exe" --remove-boot-guard
   ```

   Không còn file exe thì dùng script đi kèm — nó không cần EVBlocker:

   ```powershell
   powershell -ExecutionPolicy Bypass -File "C:\Program Files\EVBlocker\remove-boot-guard.ps1"
   ```

   Mạng về ngay, không cần khởi động lại. Việc chặn trong tường lửa vẫn giữ nguyên.
3. Mở EVBlocker bằng quyền quản trị: nó cài lại khoá và task cho lần khởi động sau.

Xem khoá có đang tồn tại không: `netsh wfp show state file=wfp.xml`, rồi tìm
`EVBlocker boot guard` trong file. Đừng dùng `show filters` — lệnh đó giấu các filter bị filter
khác trong cùng sublayer che, tức là giấu gần hết khoá.

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

**Khoá lúc khởi động chỉ có khi đang chặn, và chỉ chặn chiều ra.** Nó đóng khe hở vài giây lúc
khởi động mà Windows để mở (xem [Khoá lúc khởi động](#khoá-lúc-khởi-động)). Một phần mềm có quyền
admin tự gỡ được nó, như gỡ được rule.

**Nếu service Base Filtering Engine (BFE) khởi động lại mà máy không khởi động lại**, khoá bật lên
và máy mất mạng tới lần khởi động sau, hoặc tới khi mở EVBlocker bằng quyền quản trị. BFE là
service lõi, Windows không cho dừng khi đang chạy bình thường, nên chuyện này gần như chỉ xảy ra
khi có người cố tình can thiệp. Cách xử lý như mục
[Mất mạng sau khi khởi động lại](#mất-mạng-sau-khi-khởi-động-lại).

**UDP không thấy được đích ở tab Đang kết nối.** Windows không cung cấp địa chỉ đích trong bảng
UDP, kể cả với socket đã kết nối. Nghĩa là QUIC/HTTP3 và DNS không xác định được đích bằng cách
quét. Mục **Đã thử kết nối** thì có — event 5156/5157 *có* kèm địa chỉ đích cho UDP.

## Cập nhật

**Cài đặt → Kiểm tra bản mới.** Chỉ chạy khi bạn bấm.

Ứng dụng này tồn tại để ngăn phần mềm tự ý liên hệ internet. Một ứng dụng tự gọi server mỗi lần
mở để hỏi về chính nó là đang làm đúng thứ nó được cài để ngăn.

Nó báo và mở trang phát hành; việc tải và thay file do bạn làm thủ công.
