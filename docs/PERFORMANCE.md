# Hiệu năng — đo, không đoán

> Mọi con số dưới đây là **đo thật** trên máy phát triển (Apple Silicon, toàn bộ hạ tầng chạy
> trong Docker trên cùng một máy), ngày **05/08/2026**. Tái lập: `./deploy/bench/run-bench.sh`.
>
> Đây **không** phải benchmark production: single-node, không tách máy, dùng Service Bus
> *emulator* chứ không phải Azure thật. Giá trị của nó là **so sánh trước/sau** trên cùng một
> môi trường và **tìm ra nút thắt**, không phải để tuyên bố năng lực tuyệt đối.

---

## 1. Vì sao phải đo trên dữ liệu lớn

Bảng vài chục dòng thì Seq Scan cũng dưới 1ms — mọi thiết kế index đều "có vẻ ổn" và không thể
phân biệt tốt với tồi. Nên bộ đo dựng **200.000 hợp đồng / 600.000 hồ sơ bồi thường**, quy mô một
công ty bảo hiểm cỡ vừa sau vài năm (`deploy/bench/seed-bench-data.sql`).

---

## 2. Query optimization — 3 nghi vấn, chỉ 1 đúng

Tôi nghi 3 câu truy vấn thiếu index. Đo bằng `EXPLAIN (ANALYZE, BUFFERS)` rồi mới sửa:

| Truy vấn | Nghi ngờ | Đo được | Kết luận |
|---|---|---|---|
| `WHERE PolicyHolderId ORDER BY UpdatedAt DESC` | thiếu composite index | **0,78 ms** | Không sửa |
| `WHERE ClaimantId ORDER BY CreatedAt DESC` | thiếu composite index | **0,81 ms** | Không sửa |
| `ORDER BY CreatedAt DESC` (hàng đợi giám định) | không lọc, không giới hạn | **461 ms** | **Lỗi thật** |

**Hai nghi vấn đầu sai.** Mỗi khách chỉ có 10–30 bản ghi, sort trên 30 dòng là miễn phí. Thêm
composite index ở đó chỉ làm chậm ghi và tốn đĩa mà không đổi lại gì. Đo trước, sửa sau — nếu
làm ngược lại thì đã thêm 2 index vô ích và vẫn không thấy lỗi thật.

### 2.1 Lỗi thật: hàng đợi giám định trả về cả bảng

```csharp
// Trước — không filter, không limit
db.Claims.AsNoTracking().OrderByDescending(c => c.CreatedAt).Select(...).ToListAsync()
```

```
Gather Merge  (actual time=269..436 rows=600000)
  ->  Sort  Sort Key: "CreatedAt" DESC
        Sort Method: external merge  Disk: 33864kB      ← tràn ra ĐĨA
        ->  Parallel Seq Scan on claims (rows=200000 loops=3)
Execution Time: 461.007 ms
```

Không chỉ chậm: nó trả về **toàn bộ 600.000 dòng ≈ 104 MB** dữ liệu thô (1,4 giây chỉ để truyền
qua psql — qua EF materialize thành object rồi serialize JSON còn tệ hơn nhiều). Một người dùng
bấm F5 vài lần là đủ kéo sập service.

### 2.2 Composite index — thêm vào và **planner bỏ qua**

Phản xạ đầu tiên là `CREATE INDEX ON claims (Status, CreatedAt DESC)`. Kết quả: vẫn Seq Scan.

Lý do: trạng thái chờ chiếm **~40% bảng**. Với độ chọn lọc thấp như vậy, đi qua index rồi nhảy
về heap từng dòng còn đắt hơn quét tuần tự — planner tính đúng, index sai. Đây là chỗ dễ tưởng
"đã thêm index rồi thì phải nhanh" mà không kiểm tra kế hoạch thực thi.

### 2.3 Cái đúng: partial index

```sql
CREATE INDEX "IX_claims_PendingQueue" ON claims ("CreatedAt" DESC)
  WHERE "Status" IN ('Submitted','UnderReview');
```

```
Limit  (actual time=0.041..0.081 rows=50)
  ->  Index Scan using "IX_claims_PendingQueue" on claims
Execution Time: 0.103 ms
```

Index chỉ chứa các dòng đang chờ và **đã sẵn thứ tự** `CreatedAt DESC`, nên `LIMIT 50` đọc đúng
50 mục đầu rồi dừng — **không còn node Sort**. Index chỉ **5 MB** cho bảng 109 MB.

| | Trước | Sau |
|---|---|---|
| Thời gian truy vấn | 461 ms | **0,103 ms** (~4.500×) |
| Số dòng trả về | 600.000 | 50 (mặc định), trần 200 |
| Sort | tràn 33 MB ra đĩa | không có |

Điều kiện `WHERE` trong C# **phải khớp đúng** filter của index, lệch một chút là Postgres không
dùng được index nữa.

### 2.4 Chặn trên số dòng cho mọi endpoint danh sách

`QueryLimits`: mặc định 50, tối đa 200. Không có trần thì chỉ cần một lỗi ở frontend là kéo cả
bảng — cách API danh sách làm sập DB thường gặp nhất. Kiểm chứng: `?limit=99999` → trả về 200 dòng.

---

## 3. Load test đường đọc

`GET /api/claims` (vai giám định viên), 4.000 request / 50 luồng đồng thời, trên nền 600k hồ sơ:

| | |
|---|---|
| Throughput | **1.602 req/giây** |
| p50 / p95 / p99 | 24 / 58 / 119 ms |
| Lỗi | **0** |

---

## 4. Đường ghi — nút thắt thật nằm ở outbox

`POST /api/transfers`, 1.000 lệnh / 25 luồng: API nhận **364 req/giây, p95 131 ms, 0 lỗi**.

Nhưng HTTP trả về nhanh không có nghĩa hệ thống theo kịp — lệnh chuyển tiền chỉ **xong thật** khi
outbox đẩy được sự kiện đi và saga hoàn tất. Đo tốc độ đẩy: **8 sự kiện/giây**.

**364 vào, 8 ra.** Chênh ~45 lần: dưới tải thật hàng đợi phình vô hạn, độ trễ saga tăng không
giới hạn. Outbox vẫn "đúng" về mặt dữ liệu nhưng vô dụng về mặt vận hành. Đây là loại lỗi mà
test chức năng không bao giờ thấy, chỉ load test mới lộ.

### 4.1 Giả thuyết đầu tiên — sai

Nhìn code thấy ngay `LIMIT 20` + `Task.Delay(1 giây)` ⇒ trần cứng 20 sự kiện/giây. Sửa thành
batch 200 + *drain mode* (hút đầy batch thì lặp ngay, chỉ ngủ khi hàng đợi cạn).

Kết quả: 108 giây → 44 giây. Có cải thiện, nhưng vẫn chỉ ~24 sự kiện/giây, **kém xa** mức đáng ra
phải đạt. Vậy trần polling không phải nguyên nhân chính.

### 4.2 Đo mới tìm ra nguyên nhân thật

Thêm `Stopwatch` tách riêng thời gian từng kênh thay vì suy đoán từ dấu thời gian của log:

```
Outbox published 200 event(s) — ServiceBus 13045ms · Kafka 16ms
```

Toàn bộ chi phí nằm ở Service Bus, và lý do là **mỗi message một lượt đi-về mạng**:

```csharp
foreach (var row in rows)
    await _sender.SendMessageAsync(message, ct);   // 200 message = 200 round-trip
```

Kafka cũng vậy: `ProduceAsync` chờ delivery report của từng message.

Sửa đúng chỗ:
- **Service Bus** → `CreateMessageBatchAsync` + `SendMessagesAsync`: gộp cả lô thành 1 lượt đi-về
  (xử lý cả trường hợp batch đầy → gửi rồi mở batch mới).
- **Kafka** → `Produce` (không await, librdkafka tự gộp) + **`Flush` một lần** ở cuối lô.
- **Outbox** → một `UPDATE ... WHERE id = ANY(@Ids)` cho cả lô thay vì 200 lệnh riêng.

| Lô 200 sự kiện | Trước | Sau |
|---|---|---|
| Service Bus | ~13.000 ms | **trung vị 124 ms** |
| Kafka | (gộp trong trên) | 12–58 ms |
| Nhịp đẩy | 8 sự kiện/giây | **~1.600 sự kiện/giây** (trung vị) |

### 4.3 Phần chưa giải thích được — nói thẳng

Trung vị 124 ms/lô, nhưng thỉnh thoảng có lô **treo ~13 giây** (một lần 27 giây). Trong 19 lô đo
được, vài lô stall này chiếm gần như toàn bộ 80,9 giây tổng thời gian.

Đã kiểm tra: emulator không log lỗi, client Polly không ghi nhận retry nào. Nhiều khả năng là
giới hạn của **Service Bus emulator** (container dev, single-node) chứ không phải code — nhưng
**tôi chưa kiểm chứng trên Azure Service Bus thật**, nên không khẳng định chắc.

### 4.4 Consumer là nút thắt tiếp theo

Sau khi sửa outbox, phía tiêu thụ (Accounts) xử lý ~**14 message/giây** — giờ nó mới là chỗ chậm nhất.
`MaxConcurrentCalls` đã là 4; hướng đi tiếp theo là `PrefetchCount` và tăng concurrency.

**Chưa làm, có chủ ý:** số đo này bị nhiễu bởi chính các stall của emulator ở §4.3. Tuning theo
một phép đo không đáng tin sẽ lặp lại đúng sai lầm ở §2.2 — sửa khi chưa có bằng chứng. Việc này
chờ đo lại trên Azure thật.

---

## 5. Tính đúng đắn dưới tải

Nhanh mà sai tiền thì vô nghĩa. Sau **6.005 lệnh chuyển tiền** liên tiếp qua các đợt load test:

| Kiểm tra | Kết quả |
|---|---|
| Tổng số dư các tài khoản | 15.000.000 — **đúng bằng ban đầu** (không tạo/mất tiền) |
| Số bút toán | 12.010 = 6.005 × 2 |
| Tổng Nợ − tổng Có | **0** |
| `TransferId` không có đúng 2 bút toán | **0** |
| Dòng inbox (`processed_messages`) | 6.005 — đúng 1/lệnh, **không xử lý trùng lần nào** |
| Lệnh kẹt ở `Initiated` | **0** — saga đóng trạng thái toàn bộ |

Bút toán kép cân bằng và inbox không trùng dòng nào chứng minh idempotency hoạt động thật dưới
tải đồng thời, không chỉ trong unit test.

---

## 6. Rút ra

1. **Đo trước khi sửa.** 2/3 nghi vấn về index là sai; nút thắt outbox nằm ở chỗ khác hẳn giả
   thuyết ban đầu. Sửa theo trực giác là thêm index vô ích và bỏ sót lỗi thật.
2. **Index không tự động nhanh.** Độ chọn lọc thấp ⇒ planner bỏ qua index. Phải đọc kế hoạch
   thực thi, không dừng ở việc "đã tạo index".
3. **API nhanh không đồng nghĩa hệ thống theo kịp.** Trong kiến trúc event-driven phải đo cả
   nhịp tiêu thụ, nếu không sẽ có một hàng đợi phình âm thầm.
4. **Mỗi message một round-trip là kẻ giết hiệu năng.** Gộp lô ở ranh giới mạng đổi 8 → ~1.600
   sự kiện/giây mà không đụng gì tới ngữ nghĩa.
5. **Nói rõ chỗ chưa biết.** Stall của emulator và giới hạn consumer được ghi lại đúng như đo
   được, không tô hồng cũng không giả vờ đã giải quyết.
