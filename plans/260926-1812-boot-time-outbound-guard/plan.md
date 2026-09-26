# Khoá outbound lúc khởi động (boot guard)

**Status:** Đã duyệt 2026-09-26. Phase 1 xong (34/34 trên máy thật). Phase 2-4 chưa làm.
**Ngày tạo:** 2026-09-26
**Yêu cầu:** "Khi khởi động cũng không được app nào đã chặn internet có thể vào trong một thoáng chốc."

## Vấn đề (đã đo, không suy luận)

`netsh wfp show filters`, kiểm lại bằng `show state` (916 filter, đầy đủ), trên máy này (xem [report](../reports/researcher-260926-1800-boot-time-outbound-gap.md), mục Addendum):

- Rule của EVBlocker và filter "Default Outbound" của firewall **không PERSISTENT** — MpsSvc
  nạp lại mỗi lần boot.
- Filter "Boot Time Filter" của Windows chỉ **chặn chiều vào**. Chiều ra (`ALE_AUTH_CONNECT`)
  chỉ có PERMIT.
- ⇒ Outbound **mở** từ lúc `tcpip.sys` lên tới lúc MpsSvc áp xong `DefaultOutboundAction=Block`.
  Service/driver khởi động sớm lọt được.

Firewall rule (API EVBlocker đang dùng) **không tạo được** filter boot-time — Microsoft ghi rõ.
Phải gọi thẳng WFP (`fwpuclnt.dll`).

## Thiết kế: khoá + chốt mở

Filter PERSISTENT có hiệu lực **ngay khi cài**, không riêng lúc boot — khoá thô sẽ cắt mạng tức
thì. Nên dùng hai mảnh trong một sublayer riêng của EVBlocker:

| Filter | Loại | Tác dụng |
|---|---|---|
| **Khoá** — block mọi outbound | BOOTTIME + PERSISTENT (v4, v6) | Chặn từ lúc `tcpip.sys` lên, sống qua reboot |
| Cho qua loopback, DHCP | BOOTTIME + PERSISTENT | Máy vẫn lấy được IP trong lúc khoá |
| **Chốt mở** — permit mọi thứ, weight cao nhất trong sublayer | **Không** persistent (mất khi reboot) | Vô hiệu khoá khi máy đang chạy |

- Khi chạy bình thường: chốt mở thắng trong sublayer của EVBlocker ⇒ sublayer này không chặn gì;
  quyết định thuộc về firewall như hiện nay. Permit mềm không ghi đè được block của firewall.
- Khi reboot: chốt mở biến mất ⇒ khoá chặn mọi outbound. Task SYSTEM lúc khởi động chờ MpsSvc
  chạy và `DefaultOutboundAction=Block` đọc được, **rồi mới** thêm lại chốt mở.
- Khoá chỉ tồn tại khi đang bật chặn: cài trong `Enable`, gỡ trong `Disable` và khi dead-man
  revert chạy.

## Đổi lại (người dùng đã chấp nhận)

- Mỗi lần mở máy, **cả app được phép** mất mạng thêm vài giây, tới khi task mở chốt.
- Task hỏng / exe mất ⇒ mất mạng sau reboot tới khi chạy lệnh khôi phục. Giảm rủi ro bằng: exe
  cố định trong `Program Files`, hết giờ chờ thì vẫn mở chốt (ghi log), script khôi phục độc lập.

## Phases

| # | Phase | Phụ thuộc |
|---|---|---|
| 1 | [Lõi WFP: cài/gỡ khoá, chốt mở, trạng thái](phase-01-wfp-guard-core.md) — **xong** | — |
| 2 | [Gắn vào vòng đời bật/tắt chặn + task khởi động](phase-02-lifecycle-wiring.md) | 1 |
| 3 | [Đường khôi phục + tài liệu](phase-03-recovery-and-docs.md) | 1 |
| 4 | [Kiểm chứng trên máy thật, có reboot](phase-04-verification.md) | 2, 3 |

## Tiêu chí chấp nhận

1. Bật chặn rồi reboot: audit 5157 có sự kiện bị chặn với filter của EVBlocker **trước** khi
   MpsSvc áp policy; sau khi task mở chốt, app được phép ra mạng, app không được phép thì không.
2. Đang chạy bình thường, cài khoá không làm đổi kết quả của bất kỳ kết nối nào.
3. Tắt chặn / dead-man revert ⇒ không còn filter nào của EVBlocker trong `netsh wfp show state` (`show filters` che mất filter bị che bởi filter nặng hơn).
4. Script khôi phục gỡ sạch khoá trên máy không còn `EVBlocker.exe`.
5. 353 test cũ vẫn qua; phần dựng filter có unit test.

## Câu hỏi mở

- ~~Filter BOOTTIME có dùng được sublayer/provider riêng không?~~ **Được** — BFE nhận, đã kiểm
  trên máy thật (phase 1).
- Thời gian chờ tối đa trước khi "mở chốt dù MpsSvc chưa lên": đề xuất 3 phút.
