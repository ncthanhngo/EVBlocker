# Kiến trúc

Dành cho người bảo trì. Hướng dẫn cho người dùng ở [huong-dan-su-dung.md](huong-dan-su-dung.md).

## Cấu trúc

```
src/EVBlocker.Core/     class library — không phụ thuộc UI, unit test được
  Monitor/    quét socket đang mở (P/Invoke iphlpapi), liệt kê ứng dụng đang chạy
  History/    đọc event WFP 5157/5156, map path kernel sang ổ đĩa
  Audit/      bật/đọc audit policy qua auditpol
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
tests/                  271 test, không cần admin
```

Mọi thành phần chạm Windows API đều nằm sau interface. Đó là lý do 271 test chạy được mà không
cần quyền admin, và cũng là ranh giới quyết định cái gì test được, cái gì không.

## Bốn cách nói chuyện với Windows, và vì sao

| Cơ chế | Dùng cho | Vì sao không dùng cái khác |
|---|---|---|
| **COM** `INetFwPolicy2` | Đọc/ghi rule, đọc/ghi default action | Nhanh, không spawn process. Interop sinh bằng **CsWin32** từ metadata Win32 chính thức |
| **P/Invoke** `iphlpapi` | Bảng TCP/UDP đang mở | Poll mỗi giây; đo được **0 ms** cho 144 socket |
| **`netsh`** | Export/import cấu hình firewall | COM API **không có** export/import |
| **`schtasks`** | Dead-man switch, task khởi động | Chỉ cần exit code. Đo được `/Query` trả **exit 1** khi task không tồn tại, không phụ thuộc ngôn ngữ |

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
đọc trạng thái → nạp baseline → BACKUP → ghi rule → ARM revert → MỚI chặn
```

Chặn là bước **cuối cùng**, sau khi đường lui đã tồn tại. Tiến trình chết sau khi arm thì revert
vẫn chạy; chết trước đó thì chưa có gì bị chặn. Có test riêng cho từng nhánh hỏng.

## Reconciler cố ý không làm gì

Chỉ đối chiếu **rule**, không bao giờ đụng `DefaultOutboundAction`. Không có bản ghi ý định, nên
profile đang không chặn **không phân biệt được** với profile người dùng chủ động tắt. Để nguyên
là nghiêng về phía mạng còn chạy được.

Nếu không đọc được allow-list thì **không thay đổi gì** và thoát với exit code khác 0. Không biết
chính sách là gì khác với chính sách rỗng — áp cái rỗng sẽ xoá mọi rule người dùng đang dựa vào.

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
dotnet test                                                              # 271 test, không cần admin
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
