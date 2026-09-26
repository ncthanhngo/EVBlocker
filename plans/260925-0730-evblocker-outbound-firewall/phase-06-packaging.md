# Phase 06 — Đóng gói

**Status:** chưa làm — làm được song song sau Phase 02
**Chạm firewall:** không

## Requirements

1. Self-contained single-file, chạy trên máy trắng không cần cài .NET.
2. Dung lượng ≤ 80 MB.
3. Cùng 1 exe phục vụ cả 3 chế độ: UI (mặc định), `--reconcile`, `--revert`.

## Implementation steps

1. **Publish profile** trong `EVBlocker.App.csproj`:

   ```
   PublishSingleFile=true
   SelfContained=true
   RuntimeIdentifier=win-x64
   PublishTrimmed=true
   TrimMode=partial
   EnableCompressionInSingleFile=true
   InvariantGlobalization=true
   ```

   `TrimMode=partial`, **không** `full`: WPF dùng reflection nhiều, trim full sẽ crash lúc runtime
   ở những đường code không được test. Partial an toàn hơn, đổi lại file to hơn.

   `PublishReadyToRun=true` nếu cần startup nhanh hơn — nhưng làm file to thêm đáng kể. Đo rồi quyết.

2. **Không dùng NativeAOT** — WPF không hỗ trợ.

3. **Tránh phá trimming:** không `dynamic`, không `BinaryFormatter`, JSON dùng
   `System.Text.Json` với **source generator** (`JsonSerializerContext`) chứ không reflection.
   Đây cũng là lý do Phase 02 chọn `[GeneratedComInterface]` thay vì COM qua `dynamic`.

4. **Kiểm dung lượng trong CI** — bước build fail nếu vượt ngưỡng, để không âm thầm phình.

5. **Ký số:** chưa có cert. Không ký → SmartScreen sẽ cảnh báo khi chạy trên máy lạ. Ghi nhận,
   không giả vờ đã giải quyết.

## Tests / validation

| Kiểm tra | Cách |
|---|---|
| Chạy trên máy trắng | test trên VM Windows 11 sạch, **không** cài .NET runtime |
| Dung lượng | đo file publish, so với ngưỡng 80 MB |
| Trim không phá runtime | chạy hết 3 tab UI + cả `--reconcile` trên bản publish (không phải bản Debug) |
| 3 chế độ | `EVBlocker.exe`, `--reconcile`, `--revert` đều đúng hành vi trên bản single-file |

## Risks / rollback

- Trim phá reflection chỉ lộ **lúc runtime**, không lộ lúc build → bắt buộc test trên bản
  publish, không chỉ bản Debug.
- Single-file giải nén ra temp khi chạy; một số môi trường khoá temp sẽ fail. Có thể cần
  `IncludeNativeLibrariesForSelfExtract`.
