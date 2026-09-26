# Quét USB: phát hiện và cách ly virus ẩn file

Trạng thái: xong

## Vì sao

`a.bat.bat` ngoài Desktop chỉ **khôi phục** thư mục bị ẩn — giành quyền sở hữu rồi `attrib -h -s`.
Nó không phát hiện gì, không gỡ gì; file virus vẫn nằm nguyên trên USB và lần cắm sau lại ẩn tiếp.

## Loại virus nhắm tới

Họ worm lây qua USB kiểu shortcut (Gamarue, Dorkbot, Houdini, Bondat và tương tự). Cách hoạt động
gần như giống nhau:

1. Đặt thư mục gốc của người dùng thành `Hidden + System`
2. Tạo `.lnk` **trùng tên** thư mục vừa ẩn, đích trỏ tới `cmd.exe` / `wscript.exe` kèm tham số
   chạy payload rồi mới mở thư mục thật — nên người dùng thấy "vẫn vào được thư mục"
3. Payload là `.exe` / `.vbs` / `.js` / `.scr` nằm ẩn ở gốc hoặc trong thư mục ẩn
4. Đôi khi kèm `autorun.inf`

## Hai cái bẫy đã xác minh trên máy này

**`DriveType = 2` bỏ sót ổ cứng USB.** Ổ E: là Lenovo thinkplus HD210 cắm qua USB, nhưng
`Win32_LogicalDisk.DriveType` trả về **3 (ổ cứng)**. Phải map ổ logic → phân vùng → đĩa vật lý và
đọc `Win32_DiskDrive.InterfaceType`, nếu không sẽ bỏ qua đúng loại ổ hay mang virus nhất.

**Thư mục `Hidden + System` phần lớn là hợp lệ.** Ngay trên E: sạch đã có `System Volume
Information` và `$RECYCLE.BIN`; kèm theo `.Spotlight-V100`, `.Trashes`, `._*` do macOS tạo. Dò
theo thuộc tính ẩn mà không loại trừ danh sách này thì mọi USB đều bị báo nhiễm.

## Quy tắc phát hiện

Xếp theo độ tin cậy, mỗi dấu hiệu ghi rõ mức:

| Dấu hiệu | Mức |
|---|---|
| `.lnk` trùng tên với thư mục `Hidden+System` cùng chỗ | Chắc chắn |
| `.lnk` có đích hoặc tham số gọi `cmd/wscript/cscript/powershell/mshta/rundll32` | Chắc chắn |
| File thực thi/script mang thuộc tính `Hidden+System` ở thư mục gốc | Nghi ngờ |
| `autorun.inf` ở thư mục gốc | Nghi ngờ |
| Thư mục `Hidden+System` không nằm trong danh sách hợp lệ | Chỉ báo, không phải virus |

Đọc `.lnk` bằng cách phân tích định dạng MS-SHLLINK để lấy `RELATIVE_PATH` và
`COMMAND_LINE_ARGUMENTS`, không dùng COM — phân tích được thì test được bằng byte dựng sẵn.

## Xử lý

**Cách ly, không xoá.** File nghi ngờ được **chuyển** vào
`%ProgramData%\EVBlocker\quarantine\<thời điểm>\` kèm manifest JSON ghi đường dẫn gốc, nên hoàn tác
được. Xoá thẳng một file trên USB của người khác là việc không lùi lại được, và một lần báo nhầm
là mất dữ liệu.

**Khôi phục thư mục** bị ẩn: gỡ `Hidden + System`, bỏ qua danh sách hợp lệ. Đây là phần
`a.bat.bat` đang làm, tự động và không phải chọn tay.

## Phạm vi — nói rõ cái KHÔNG làm

Đây **không phải phần mềm diệt virus**. Nó xử lý phần nằm trên USB. Nếu payload đã chạy trên máy
thì việc dọn máy là việc của Defender hoặc công cụ chuyên dụng; ứng dụng này không quét bộ nhớ,
không đọc registry autorun, không gỡ thứ đã cài vào máy.

Phần chặn mạng sẵn có giúp một nửa còn lại: họ worm này đều gọi về máy chủ điều khiển, nên khi
bật chặn thì payload có chạy cũng không ra được internet.

## Các bước

- [x] Xác minh hai cái bẫy ở trên trên máy thật
- [x] Core: dò ổ USB qua bus type đọc bằng `DeviceIoControl`, không qua `DriveType`
- [x] Core: phân tích `.lnk` theo MS-SHLLINK
- [x] Core: quy tắc phát hiện + test
- [x] Core: cách ly có manifest, hoàn tác được
- [x] App: mục USB, tự quét khi cắm ổ qua WM_DEVICECHANGE

## Kết quả kiểm chứng

**Ổ thật (E:, Lenovo thinkplus HD210 qua USB):** dò đúng là "ổ cứng USB" nhờ đọc bus type — nếu
theo `DriveType` thì đã bỏ sót. Quét 10 thư mục trong **3 ms**, 0 phát hiện, khớp với kiểm tra
bằng tay.

**Ổ nhiễm dựng giả**, `.lnk` do chính Windows tạo qua WScript.Shell:

| Phát hiện | Mức | Xử lý |
|---|---|---|
| `Photos.lnk` trùng tên thư mục bị ẩn | Chắc chắn | cách ly |
| `autorun.inf` | Đáng ngờ | cách ly |
| `svhost.vbs` ẩn | Đáng ngờ | cách ly |
| `Photos`, `Tai lieu` bị ẩn | Chỉ báo | cho hiện lại |

Sau khi dọn: hai thư mục hiện lại đủ nội dung, `readme.txt` và `setup-that.exe` không bị đụng
tới, quét lại ra 0 phát hiện, manifest ghi đủ 3 file kèm đường dẫn gốc.
