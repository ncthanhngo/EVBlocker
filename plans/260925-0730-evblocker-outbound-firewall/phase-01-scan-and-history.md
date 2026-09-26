# Phase 01 — Scaffold + Scan + History

**Status:** code xong, đã verify. Chưa chạy `auditpol /set` (cần admin).
**Chạm firewall:** không. Chỉ bật audit policy (không đổi rule, không đổi default action).

## Kết quả verify (2026-09-25)

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build` | 0 warning (TreatWarningsAsErrors bật) |
| `dotnet test` | 79/79 pass |
| Scanner vs `Get-NetTCPConnection` | **37/37 dòng của scanner đều có trong output PowerShell, 0 dòng lạ** → offset struct, decode port, parse địa chỉ đều đúng |
| GUID subcategory | Xác minh bằng `auditpol /list /subcategory:* /v`: `{0CCE9226-69AE-11D9-BED3-505054503030}` đúng |

**Khác biệt đã biết, lành tính:** PowerShell trả thêm 12 socket ở state `Bound` mà
`GetExtendedTcpTable` không trả về. Socket `Bound` chưa listen/connect, không có remote address,
nên không liên quan đến mục tiêu "app nào ra internet".

**Chưa verify được:** `auditpol /get|/set` và `WfpEventLogReader` — cả hai cần admin, session
verify không elevated. Parse logic của chúng đã test bằng dữ liệu mẫu.

## Requirements

1. Solution build được, có Core + App + Tests.
2. Liệt kê được process đang có kết nối TCP/UDP ra **IP public** (loại loopback, private, link-local, multicast).
3. Đọc được history app đã bị chặn từ Security event log (5157) và được phép (5156).
4. Bật/tắt/kiểm tra audit policy cho subcategory "Filtering Platform Connection".
5. Core không phụ thuộc WPF; mọi lời gọi Windows API nằm sau interface.

## Files tạo mới

```
EVBlocker.sln
Directory.Build.props                     # TFM, nullable, warnings-as-errors, trim analyzer
.gitignore
src/EVBlocker.Core/EVBlocker.Core.csproj
src/EVBlocker.Core/Monitor/ConnectionRecord.cs          # + TcpConnectionState, TransportProtocol
src/EVBlocker.Core/Monitor/IConnectionScanner.cs
src/EVBlocker.Core/Monitor/ActiveConnectionScanner.cs
src/EVBlocker.Core/Monitor/IpHlpApi.cs                  # P/Invoke + layout offset
src/EVBlocker.Core/Monitor/AddressClassifier.cs         # public vs private IP
src/EVBlocker.Core/Monitor/ProcessPathResolver.cs
src/EVBlocker.Core/History/NetworkAttempt.cs            # + AttemptSummary
src/EVBlocker.Core/History/IDevicePathMapper.cs
src/EVBlocker.Core/History/DevicePathMapper.cs           # \device\... -> C:\
src/EVBlocker.Core/History/WfpEventParser.cs             # parse thuần, test được
src/EVBlocker.Core/History/IAttemptHistoryReader.cs
src/EVBlocker.Core/History/WfpEventLogReader.cs
src/EVBlocker.Core/Audit/IAuditPolicy.cs                 # + AuditSetting
src/EVBlocker.Core/Audit/AuditPolBackupParser.cs         # parse CSV thuần, test được
src/EVBlocker.Core/Audit/AuditPolicyManager.cs
src/EVBlocker.Core/Internal/ProcessRunner.cs             # dùng lại cho netsh ở Phase 03
tests/EVBlocker.Core.Tests/EVBlocker.Core.Tests.csproj
tests/EVBlocker.Core.Tests/AddressClassifierTests.cs
tests/EVBlocker.Core.Tests/WfpEventParserTests.cs
tests/EVBlocker.Core.Tests/AuditPolBackupParserTests.cs
tests/EVBlocker.Core.Tests/ActiveConnectionScannerSmokeTests.cs
```

Lệch so với dự kiến ban đầu, có lý do:

- `BlockedAttempt` → **`NetworkAttempt`**: event 5156 (được phép) và 5157 (bị chặn) có cấu trúc y
  hệt nhau, một type phục vụ cả hai thay vì hai type gần trùng.
- Thêm `AuditPolBackupParser`: đọc trạng thái audit qua `auditpol /backup` (CSV có cột số) thay vì
  `/get` (chuỗi bị bản địa hoá) — xem phần đọc trạng thái bên dưới.
- Thêm `ProcessRunner`: Phase 03 cũng cần chạy `netsh`, nên tách sẵn.
- `IpHlpApi` dùng **hằng số offset** thay vì struct: struct IPv6 cần `fixed byte[16]`, buộc cả
  parser vào `unsafe`. Offset giữ parser là code an toàn hoàn toàn.

## Implementation steps

### 1. Scaffold

`dotnet new sln`, `classlib` cho Core (net8.0-windows), `xunit` cho tests.
`Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`.
App (WPF) tạo ở phase này nhưng để trống — UI làm ở Phase 04.

### 2. Active connection scanner

P/Invoke `iphlpapi.dll`:

- `GetExtendedTcpTable(AF_INET, TCP_TABLE_OWNER_PID_ALL)` và bản AF_INET6
- `GetExtendedUdpTable(AF_INET, UDP_TABLE_OWNER_PID)` và AF_INET6

Gọi 2 lần: lần 1 lấy size, lần 2 lấy data. Dùng `ArrayPool<byte>` để tránh cấp phát lại mỗi lần poll.

PID → exe path bằng `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName`.
Không dùng `Process.MainModule` — chậm và fail với process 64-bit/quyền khác.
Cache PID → path, invalidate theo process start time để tránh nhầm PID bị tái dùng.

UDP không có state nên không có "connection" thật — ghi nhận endpoint, đánh dấu rõ trong model.

### 3. Address classifier

Loại khỏi kết quả "ra internet": loopback, `10/8`, `172.16/12`, `192.168/16`, `169.254/16`, CGNAT `100.64/10`, multicast, broadcast, `::1`, `fc00::/7`, `fe80::/10`.
Đây là phần logic thuần — unit test đầy đủ, không cần Windows.

### 4. History reader

`EventLogQuery("Security", EventLogQueryType.LogName, xpath)` với XPath lọc `EventID=5157 or EventID=5156`.

Lấy từ event: `Application`, `ProcessId`, `Direction`, `DestAddress`, `DestPort`, `SourceAddress`.
Path trong event ở dạng kernel (`\device\harddiskvolume3\...`) → **phải map sang dạng DOS** (`C:\...`) bằng `QueryDosDevice`. Nếu không map, path không khớp với allow-list.

Chỉ lấy `Direction = Outbound` (`%%14593`).
Group theo exe path, đếm số lần, giữ lần cuối cùng.

Tách phần parse XML ra hàm thuần để unit test bằng XML mẫu, không cần event log thật.

### 5. Audit policy manager

Bật:

```
auditpol /set /subcategory:{0CCE9226-69AE-11D9-BED3-505054503030} /failure:enable
```

Dùng **GUID**, không dùng tên — tên bị bản địa hoá theo ngôn ngữ Windows.
GUID phải xác minh bằng `auditpol /list /subcategory:* /v` trước khi hardcode.

Chỉ bật `/failure` (event 5157 = bị chặn). **Không bật `/success`** ở mặc định: 5156 sinh ra cực nhiều event, đầy Security log rất nhanh.

Cần quyền admin. Phải kiểm tra và báo lỗi rõ ràng nếu không có.

## Tests / validation

| Kiểm tra | Cách |
|---|---|
| `AddressClassifier` | unit test: bảng IP public/private/loopback/v6 |
| Parse event 5157 | unit test với XML mẫu, gồm cả path dạng `\device\harddiskvolumeN` |
| Scanner chạy thật | chạy thử, so sánh với `Get-NetTCPConnection` của PowerShell |
| Audit GUID đúng | `auditpol /list /subcategory:* /v` (cần admin) |
| Build | `dotnet build` không warning; `dotnet test` xanh |

## Risks / rollback

- Bật audit làm Security log đầy nhanh → chỉ bật `/failure`, và kiểm tra `MaxSize` của log trước khi bật.
- Rollback: `auditpol /set /subcategory:{GUID} /failure:disable`. Phải lưu lại trạng thái **trước khi** thay đổi để restore đúng, không đoán.
- P/Invoke sai struct layout → đọc rác. Giảm rủi ro bằng cách so sánh output với `Get-NetTCPConnection` trên cùng thời điểm.
