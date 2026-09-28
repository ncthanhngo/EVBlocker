# Kiến trúc

Dành cho người bảo trì. Hướng dẫn cho người dùng ở [huong-dan-su-dung.md](huong-dan-su-dung.md).

## Cấu trúc

```
src/EVBlocker.Core/     class library — không phụ thuộc UI, unit test được
  Monitor/    quét socket đang mở (P/Invoke iphlpapi), liệt kê ứng dụng đang chạy, cắt kết nối TCP của một exe
  History/    đọc event WFP 5157/5156, map path kernel sang ổ đĩa
  Installed/  đọc danh sách phần mềm đã cài từ registry gỡ cài đặt
  Usb/        dò ổ USB theo bus, đọc .lnk, quy tắc phát hiện virus USB, cách ly
  Audit/      bật/đọc audit policy qua auditpol (màn hình Đã thử kết nối gọi trực tiếp)
  Firewall/   COM INetFwPolicy2 + state machine enforcement
  Policy/     allow-list store, reconcile rule, danh mục ứng dụng phổ biến + dò đường dẫn
  Baseline/   danh sách service OS (JSON)
  Safety/     backup cấu hình, dead-man switch
  Startup/    task lúc khởi động, reconciler
  Updates/    so tag GitHub với version đang chạy
  Internal/   chạy process, xoá file an toàn
src/EVBlocker.App/      WPF — chỉ gọi Core qua interface
tools/EVBlocker.Verify/ kiểm chứng các thao tác cần quyền admin
tools/capture-ui.ps1    chụp ảnh cửa sổ để kiểm giao diện sau khi sửa XAML
tests/                  353 test, không cần admin
```

Mọi thành phần chạm Windows API đều nằm sau interface. Đó là lý do 353 test chạy được mà không
cần quyền admin, và cũng là ranh giới quyết định cái gì test được, cái gì không.

## Bốn cách nói chuyện với Windows, và vì sao

| Cơ chế | Dùng cho | Vì sao không dùng cái khác |
|---|---|---|
| **COM** `INetFwPolicy2` | Đọc/ghi rule, đọc/ghi default action | Nhanh, không spawn process. Interop sinh bằng **CsWin32** từ metadata Win32 chính thức |
| **P/Invoke** `iphlpapi` | Bảng TCP/UDP đang mở | Poll mỗi giây; đo được **0 ms** cho 144 socket |
| **`netsh`** | Export/import cấu hình firewall | COM API **không có** export/import |
| **`schtasks`** | Dead-man switch, task khởi động | Chỉ cần exit code. Đo được `/Query` trả **exit 1** khi task không tồn tại, không phụ thuộc ngôn ngữ |
| **WFP** `fwpuclnt` | Khoá lúc khởi động | Firewall rule **không tạo được** filter boot-time — Microsoft ghi rõ. Interop cũng sinh bằng CsWin32 |

### Vì sao interop phải sinh tự động

`INetFwPolicy2` và `INetFwRule` là **dual interface** — vtable bắt đầu bằng IUnknown rồi IDispatch,
sau đó là *mọi* getter/setter theo đúng thứ tự IDL. Viết tay nghĩa là lệch một slot là gọi nhầm
hàm, mà nhánh ghi không test được nếu không có admin — lỗi sẽ lộ trên máy người dùng.

Quyết định này tự chứng minh ngay: CLSID nhớ từ đầu là `...F524F22CFCE2`, **giá trị đúng là
`E2B3C97F-6AE1-41AC-817A-F6F92166D7DD`**.

CsWin32 chạy lúc build (`PrivateAssets=all`), không thành dependency runtime.

## Trạng thái suy ra từ máy, không lưu file

| Quan sát | Trạng thái |
|---|---|
| Không profile nào chặn | `Off` |
| Có chặn + có task revert đang chờ | `Armed` |
| Có chặn + không có task revert | `On` |

Việc chặn nằm trong cấu hình firewall, revert đang chờ nằm trong Task Scheduler — cả hai sống lâu
hơn tiến trình và đều có thể bị người khác đổi. Một file trạng thái là **ý kiến thứ hai có thể mâu
thuẫn với cả hai**, mà đúng lúc mâu thuẫn là lúc người ta cần sự thật.

Đánh đổi: không biết **thời điểm** revert nếu không phải phiên đó hẹn. UI nói thẳng điều đó.

## Thứ tự trong `Enable()` chính là lập luận an toàn

```
đọc trạng thái → nạp baseline → BACKUP → ghi rule → ARM revert → exe cố định + task → khoá boot → MỚI chặn
```

Chặn là bước **cuối cùng**, sau khi đường lui đã tồn tại. Tiến trình chết sau khi arm thì revert
vẫn chạy; chết trước đó thì chưa có gì bị chặn. Có test riêng cho từng nhánh hỏng.

## Reconciler cố ý không làm gì

Chỉ đối chiếu **rule**, không bao giờ đụng `DefaultOutboundAction`. Không có bản ghi ý định, nên
profile đang không chặn **không phân biệt được** với profile người dùng chủ động tắt. Để nguyên
là nghiêng về phía mạng còn chạy được.

Nếu không đọc được allow-list thì **không thay đổi gì** và thoát với exit code khác 0. Không biết
chính sách là gì khác với chính sách rỗng — áp cái rỗng sẽ xoá mọi rule người dùng đang dựa vào.

## Khoá lúc khởi động (boot guard)

**Vấn đề, đo trên máy thật** (`netsh wfp show state`, 916 filter): filter sinh từ firewall rule và
filter "Default Outbound" **không có cờ PERSISTENT** — `MpsSvc` nạp lại mỗi lần boot. Filter
"Boot Time Filter" của Windows chỉ **block** ở `ALE_AUTH_RECV_ACCEPT` (chiều vào) và
`IPFORWARD`; ở `ALE_AUTH_CONNECT` (chiều ra) chỉ có PERMIT. ⇒ Chiều ra mở từ lúc `tcpip.sys` lên
tới lúc `MpsSvc` áp policy.

**Cơ chế** — sublayer riêng, weight `0xFFFF`, trên `ALE_AUTH_CONNECT_V4/V6`:

| Filter | Cờ | Weight |
|---|---|---|
| Block mọi thứ | BOOTTIME + PERSISTENT (hai bản) | 1 |
| Permit loopback, DHCP (UDP remote 67 / 547) | BOOTTIME + PERSISTENT | 8 |
| **Chốt mở** — permit mọi thứ | **không** persistent | 15 |

BOOTTIME có hiệu lực từ `tcpip.sys` tới lúc BFE lên; PERSISTENT từ lúc BFE lên. BFE chuyển giữa hai
loại **nguyên tử** (MS docs), nên không có khoảnh khắc nào không có block.

Filter PERSISTENT có hiệu lực **ngay khi thêm**, không riêng lúc boot. Chốt mở là thứ làm khoá vô
hại khi máy chạy: trong một sublayer, filter nặng nhất khớp sẽ quyết — chốt mở thắng, sublayer
không chặn gì, firewall quyết như cũ. Permit mềm (mặc định) không ghi đè được block của sublayer
firewall. Chốt mở không persistent nên mất khi reboot — đó chính là lúc khoá cần có hiệu lực.

**Mở khoá lúc boot**: task SYSTEM (BootTrigger, không delay, priority 4) poll mỗi giây tới khi đọc
được default action (tức `MpsSvc` đã lên), đối chiếu rule, rồi `SyncBootGuard` — đang chặn thì
thêm lại chốt mở, không chặn thì gỡ khoá. **Hết 3 phút hoặc có lỗi thì vẫn mở chốt** và ghi
`RELEASED WITHOUT CONFIRMING...` vào log: đây là chỗ duy nhất cố ý fail-open, vì khoá không ai mở
là máy mất mạng hẳn.

**Không bao giờ có khoá mà không có đường mở**:

- `Enable` chép exe vào `Program Files` và đăng ký task **trước** khi cài khoá; lỗi ở đó thì dừng,
  chưa chặn gì.
- Task không tắt được từ UI khi đang chặn.
- Dead-man revert có action thứ hai `EVBlocker.exe --remove-boot-guard` sau `netsh import` —
  import khôi phục default action nhưng không đụng được filter WFP.
- `Disable` gỡ khoá sau khi đã đặt Allow.
- Máy bật chặn từ trước khi có khoá: app mở bằng quyền admin sẽ `SyncBootGuard` và cài bù.
- Khôi phục tay: `--remove-boot-guard`, hoặc `remove-boot-guard.ps1` (P/Invoke qua `Add-Type`, GUID
  cố định — test đối chiếu từng GUID với `BootGuardFilters`).

**Exe cố định trong `Program Files`**: task chạy dưới SYSTEM. Trỏ vào exe ở chỗ người dùng tự để
thì (1) file bị chuyển/xoá là lần boot sau mất mạng, (2) ai ghi được file đó là chạy được code
dưới SYSTEM.

**Kiểm chứng**: `tools/EVBlocker.Verify` cài khoá, gỡ chốt mở, thử kết nối thật tới 1.1.1.1:443
(bị chặn), loopback (thông), mở chốt lại, gỡ — và đối chiếu cờ bằng `netsh wfp show state`. Chạy
qua `dotnet EVBlocker.Verify.dll` để tiến trình nằm trong allow-list.

**Bẫy khi kiểm**: `netsh wfp show filters` (kể cả `verbose=on`) trả lời "filter nào thắng" nên
giấu filter bị filter nặng hơn không điều kiện trong cùng sublayer che — khi khoá đang mở chốt,
nó chỉ thấy 2 filter chốt mở. Dùng `show state`. File đó có hai phần tử gốc (`wfpstate`,
`firewallState`), phải bọc lại trước khi parse.

## Vài điểm dễ sai

**`FirewallProfiles.All = 0x7FFFFFFF`**, không phải `Domain|Private|Public = 7`. Windows lưu giá
trị sentinel; ghi 7 sẽ đọc ra thành ba profile rời và **mọi** phép so sánh với rule đọc về đều
báo lệch, khiến applier ghi lại rule đó mãi mãi. Đã đo trên rule thật của Windows: `2147483647`.

**COM báo `Allow` ở chỗ PowerShell báo `NotConfigured`.** `NET_FW_ACTION` chỉ có Block/Allow, nên
COM trả về *hành vi hiệu dụng*. Hệ quả: `Disable()` đặt `Allow` tường minh, không đưa profile về
`NotConfigured` được. Máy độc lập thì giống hệt; máy do Group Policy quản thì khác, muốn hoàn
nguyên chính xác phải restore backup.

**Runtime map `E_ACCESSDENIED` thành `UnauthorizedAccessException`** ngay ở tầng interop, nên
`catch (COMException)` không bao giờ chạy. Phải bắt cả hai.

**Tên subcategory của auditpol bị bản địa hoá** — luôn dùng GUID. Tương tự, `"SYSTEM"` trong
định nghĩa task phải là SID `S-1-5-18`.

**Đọc trạng thái audit qua `auditpol /backup`** (CSV có cột số), không qua `/get` (chữ bị dịch).

**`RuntimeIdentifier`/`SelfContained` không phải publish-only.** Đặt chúng vào csproj sẽ đổi
đường ra của build thường sang `bin/Debug/.../win-x64/` và để lại exe cũ ở đường cũ. Chúng nằm
trong `Properties/PublishProfiles/SelfContained.pubxml`.

**WPF không hỗ trợ trimming** (`NETSDK1168`), nên 62.9 MB là sàn cho bản self-contained.

**WPF loại `System.IO` khỏi implicit usings** vì `System.Windows.Shapes.Path` đụng
`System.IO.Path`.

## Theme

Hai palette **khớp key 69/69 hai chiều**, đổi lúc chạy bằng cách thay dictionary. Chạy được vì
mọi màu đều tham chiếu bằng `DynamicResource`, còn thứ không đổi theo theme (bo góc, font) nằm
trong `Tokens.xaml` và dùng `StaticResource`. **Một `StaticResource` trỏ vào key palette sẽ giữ
nguyên màu cũ sau khi đổi** và trông như lỗi render.

Dictionary mới được thêm **trước khi** xoá cái cũ. Xoá trước sẽ có một frame không còn palette
nào, lúc đó mọi `DynamicResource` giải về rỗng và cửa sổ vẽ trắng.

## Phần mềm đã cài

Đọc ba nhánh registry gỡ cài đặt (HKLM 64-bit, HKLM 32-bit, HKCU) và áp đúng bốn điều kiện mà
Programs and Features dùng. Tái hiện đúng bộ lọc là cần thiết: đọc thô cho **168 key**, lọc xong
còn **35** — số Control Panel hiển thị.

Khoảng trống không đóng được: một mục gỡ cài đặt mô tả **phần mềm**, còn rule firewall scope theo
**file thực thi**. Đo trên máy thật: 16/35 xác định được file.

Hai bước quyết định chất lượng, và cả hai đều tìm ra bằng cách chạy thật rồi đọc kết quả:

**Loại file cài đặt.** `DisplayIcon` trỏ vào installer nhiều hơn tưởng — `OneDriveSetup.exe`,
`python-3.14.6-amd64.exe`, `VC_redist.x64.exe`. Bản đầu tiên resolve được 25/35 nhưng **11 trong
đó là file cài**; cho phép chúng sinh rule cho thứ chạy một lần rồi không bao giờ mở socket. Điều
kiện `\Package Cache\` không phải phỏng đoán: Windows Installer giữ gói gốc ở đó để sửa và gỡ.
Các điều kiện theo tên là heuristic, cố ý ngắn, và có test ghi lại một false positive được chấp
nhận có chủ ý (tên bắt đầu bằng `setup`).

**Ưu tiên file trùng tên.** Liệt kê thư mục trả lời "ở đây có gì", không trả lời "phần mềm này là
gì". OneDrive để mười binary cạnh installer và `OneDrive.exe` ở thư mục trên — lấy nguyên danh
sách sẽ cho phép mười file phụ trợ và **bỏ sót đúng tiến trình đồng bộ**. So sánh sau khi bỏ hết
ký tự không phải chữ và số, nên "Microsoft OneDrive" khớp `OneDrive.exe` mà không khớp
`OneDriveStandaloneUpdater.exe`.

Git vẫn không giải được bằng cách này: mục của nó chỉ ra launcher ở thư mục gốc, còn
`git-remote-https.exe` nằm sâu ba cấp. Danh mục mặc định mới là nơi xử lý Git.

Phần đọc registry tách khỏi phần quyết định, nên toàn bộ luật kiểm thử được mà không cần registry.

## Icon ứng dụng

Hỏi shell (`SHGetFileInfo`) chứ không đọc icon thẳng từ file. Rất nhiều exe phụ trợ không mang
icon riêng; đọc thẳng thì chúng trống, còn shell trả về icon chung giống hệt Explorer. Đo trên máy
thật: **45/45 tiến trình có icon** sau khi đổi, trước đó một phần đáng kể là ô trống.

`SHFILEINFO` được đọc theo **offset trong buffer byte**, cùng cách `IpHlpApi.cs` làm, vì hai trường
chuỗi độ dài cố định khiến struct không blittable — khai báo thành struct sẽ phải dùng `unsafe
fixed` hoặc tự viết marshalling, trong khi chỉ cần trường đầu tiên.

Chi phí đo được, mỗi đường dẫn mới: **trung vị 0,91 ms · p90 2,05 ms**, riêng lần gọi đầu tiên
trong tiến trình **55,8 ms** do khởi động shell. Kết quả được cache theo đường dẫn và `Freeze()`,
lần thứ hai là **0,01 ms cho 45 app**. Cache là bắt buộc chứ không phải tối ưu sớm: grid bật ảo
hoá kèm recycling nên converter chạy lại cho mọi dòng cuộn vào tầm nhìn.

## Build và kiểm chứng

```powershell
dotnet test                                                              # 353 test, không cần admin
dotnet publish src/EVBlocker.App -p:PublishProfile=SelfContained         # 1 file, ~63 MB

# Cần admin: kiểm các thao tác ghi mà unit test không chạm tới được
.\tools\EVBlocker.Verify\bin\Debug\net8.0-windows\EVBlocker.Verify.exe
```

`EVBlocker.Verify` **không bật default-deny**: nó đọc `DefaultOutboundAction` ở đầu và cuối, khác
nhau là FAIL. Mỗi thao tác ghi được xác nhận **độc lập qua PowerShell** — đọc lại bằng chính code
vừa ghi thì nó tự đồng ý với chính nó kể cả khi cả hai đầu đều sai.

Là console app chứ không phải `.ps1` vì PowerShell 5.1 không nạp được assembly .NET 8; script sẽ
phải gọi `netsh`/`schtasks` trực tiếp, tức là kiểm chứng Windows chứ không kiểm chứng code.

## Phát hành

Push tag `v*` → workflow chạy test → publish → kiểm dung lượng (**có cả cận trên và cận dưới** —
artifact đột nhiên nhỏ đi nghĩa là cấu hình publish đã đổi và file có thể không còn chạy được
trên máy không có .NET) → tạo release kèm exe.

Tag phải parse được thành version, vì đó chính là thứ tính năng kiểm tra cập nhật đem ra so sánh.
Tag cũng phải khớp `<Version>` trong `Directory.Build.props` — version mà exe tự báo. Workflow
dừng nếu hai thứ lệch nhau: exe tự nhận cũ hơn release của chính nó sẽ bị mời cập nhật mãi.

Phát hành bản mới: tăng `<Version>`, commit, rồi `git tag vX.Y.Z && git push origin main vX.Y.Z`.
