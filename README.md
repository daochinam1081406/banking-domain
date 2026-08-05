# banking-domain

Hệ thống **microservices .NET 9** cho nghiệp vụ ngân hàng kết hợp bảo hiểm (bancassurance).
Ba service độc lập, mỗi service một database riêng, giao tiếp bất đồng bộ qua message broker.
Duyệt hồ sơ bồi thường sẽ tự động chi tiền về tài khoản ngân hàng thông qua một saga xuyên service.

| | |
|---|---|
| **Service** | Insurance · Accounts · Payments — database riêng từng service |
| **Messaging** | Azure Service Bus (giao dịch, saga) · Apache Kafka (event stream) · Azure Event Grid |
| **Dữ liệu** | SQL Server + EF Core · PostgreSQL + Dapper · Redis |
| **Test** | 64 unit + 10 integration (Testcontainers dựng database thật) |

---

## Kiến trúc

```
                      ┌──────────────────────────────┐
                      │   Banking Console (React)    │
                      └──────┬───────┬───────┬───────┘
                             │       │       │
              ┌──────────────┘       │       └──────────────┐
              ▼                      ▼                      ▼
    ┌───────────────────┐  ┌──────────────────┐  ┌────────────────────┐
    │   Insurance.Api   │  │   Payments.Api   │  │    Accounts.Api    │
    │   PostgreSQL/EF   │  │ PostgreSQL/Dapper│  │   SQL Server/EF    │
    │  Policy · Claim   │  │ Transfer · Auth  │  │  Balance · Ledger  │
    └─────────┬─────────┘  └────┬────────┬────┘  └─────────┬──────────┘
              │                 │        │ gRPC            │
              │                 │        └─────────────────┤
              │   Outbox        │  Outbox                  │
              └────────┬────────┴──────────┬───────────────┘
                       ▼                   ▼
        ┌──────────────────────┐  ┌──────────────────────┐
        │  Azure Service Bus   │  │        Kafka         │
        │  giao dịch · saga    │  │ event stream · audit │
        │  dead-letter · retry │  │     replay được      │
        └──────────────────────┘  └──────────────────────┘
```

**Đồng bộ chỉ khi bắt buộc.** Payments gọi Accounts qua gRPC để kiểm tra số dư trước khi nhận lệnh.
Mọi trao đổi còn lại đi qua broker, nên một service chết không kéo theo service khác.

**Không service nào đọc database của service khác.** Muốn biết dữ liệu bên kia thì gọi API hoặc
nghe event. Đây là ranh giới khiến việc tách service có ý nghĩa.

---

## Nghiệp vụ

### Ngân hàng
- **Bút toán kép** — mỗi lệnh sinh đúng hai bản ghi sổ cái (Nợ + Có). Sổ cái bất biến là nguồn sự
  thật để đối soát; số dư chỉ là giá trị chốt nhanh.
- **Quyền sở hữu** — chủ tài khoản lấy từ JWT, không cho client tự khai. Đọc tài khoản người khác
  trả `404` chứ không phải `403`, để không lộ tài khoản đó có tồn tại.
- **Tiền tệ** — kiểm tra cả hai đầu trước khi ghi nợ/ghi có; cộng chéo tiền tệ là tạo tiền từ không khí.

### Bảo hiểm
- **Hợp đồng** `Draft → PremiumCollecting → Active → Expired`. Chỉ chuyển sang `Active` khi
  **đã thu được phí** qua saga trích nợ tài khoản ngân hàng — không có nút kích hoạt thủ công.
- **Hồ sơ bồi thường** `Submitted → UnderReview → Approved → Paid`, hoặc `Rejected`.
  Trạng thái `Paid` chỉ do saga đặt khi Payments báo đã chi xong.
- **Công thức chi trả** — số thực trả là `(chi phí công nhận − miễn thường) × (1 − tỷ lệ đồng chi trả)`,
  giới hạn bởi hạn mức còn lại. Hạn mức bị trừ theo **số thực trả**, không phải số khách yêu cầu.
  Có thời gian chờ đầu hợp đồng để chống trục lợi.
- **Phân tách nhiệm vụ** — khách hàng không tự duyệt hồ sơ của mình; chỉ vai trò `adjuster` mới
  được duyệt hoặc từ chối.

### Bancassurance
Hai chiều tiền giữa ngân hàng và bảo hiểm: **thu phí** trích nợ tài khoản khách sang quỹ bảo hiểm,
và **chi trả bồi thường** từ quỹ bảo hiểm về tài khoản khách.

---

## Độ tin cậy

| Cơ chế | Giải quyết vấn đề gì |
|---|---|
| **Outbox** | Ghi dữ liệu và ghi outbox trong cùng một transaction, tiến trình nền mới đẩy lên broker. Không còn cảnh dữ liệu đã đổi mà event bị mất. |
| **Inbox** | Broker chỉ đảm bảo at-least-once. Dấu inbox ghi cùng transaction với thay đổi dữ liệu, dùng khoá chính của database làm trọng tài — không phải kiểm tra trong bộ nhớ. |
| **Saga** | Mọi lệnh đều có trạng thái cuối. Lỗi nghiệp vụ phát event bù trừ; lỗi hạ tầng trả message cho broker giao lại. |
| **Optimistic concurrency** | `rowversion` trên SQL Server, system column `xmin` trên PostgreSQL. |
| **Correlation ID** | Một mã đi xuyên HTTP → gRPC → outbox → message → consumer, để lần ra được một giao dịch. |
| **Event versioning** | Mỗi message mang `SchemaVersion`; consumer gặp version lạ thì dead-letter kèm lý do, không đoán mò trên dữ liệu tiền tệ. |

## Bảo mật

- Mật khẩu băm **PBKDF2-SHA256 600.000 vòng** với salt riêng từng người dùng.
- **Refresh token xoay vòng**; dùng lại token đã thu hồi thì thu hồi toàn bộ chuỗi token của người đó.
  Database chỉ lưu bản băm SHA-256.
- **Chống dò mật khẩu** bằng bộ đếm trên Redis theo cả tài khoản lẫn IP, vượt ngưỡng trả `429`
  kèm `Retry-After`. Đếm cả tài khoản không tồn tại để không lộ tài khoản nào có thật.
- **Idempotency-Key** cho các lệnh tạo giao dịch, client retry không tạo lệnh trùng.

---

## Chạy local

Không cần tài khoản Azure — dùng Service Bus emulator chạy trong Docker.

```bash
docker compose up -d --build
```

Compose có `healthcheck` nên các service tự chờ database sẵn sàng rồi mới khởi động.
Đợi tới khi `docker compose ps` báo các hạ tầng đều `healthy`.

Tài khoản seed sẵn lúc khởi động:

| Tài khoản | Mật khẩu | Vai trò |
|---|---|---|
| `demo` | `Demo@123` | Khách hàng |
| `alice` | `Alice@123` | Khách hàng |
| `adjuster` | `Adjuster@123` | Giám định viên |

### Thử luồng chuyển tiền

```bash
# 1) Đăng nhập
TOKEN=$(curl -s -X POST http://localhost:8081/auth/login -H "Content-Type: application/json" \
  -d '{"username":"demo","password":"Demo@123"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")

# 2) Chuyển tiền — trả 202 ngay, việc ghi sổ diễn ra bất đồng bộ
curl -X POST http://localhost:8081/api/transfers -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"fromAccount":"ACC-001","toAccount":"ACC-002","amount":500000,"currency":"VND"}'

# 3) Vài giây sau, Accounts đã consume event và cập nhật số dư
curl http://localhost:8082/api/accounts/ACC-002 -H "Authorization: Bearer $TOKEN"
```

### Giao diện

```bash
cd frontend && npm install && npm run dev    # http://localhost:5174
```

Banking console: đăng nhập, xem số dư và sao kê bút toán kép, chuyển tiền, quản lý hợp đồng và
hồ sơ bồi thường. Đăng nhập bằng `adjuster` sẽ thấy hàng đợi giám định thay vì danh sách hồ sơ cá nhân.

### Cổng

| Service | REST | Ghi chú |
|---|---|---|
| Payments | `8081` | Xác thực, chuyển tiền |
| Accounts | `8082` | Số dư, sổ cái · gRPC `8090` (h2c, nội bộ) |
| Insurance | `8083` | Hợp đồng, bồi thường |
| Frontend | `5174` | Vite dev server |

Hạ tầng dùng cổng lệch mặc định để tránh đụng dịch vụ sẵn có trên máy:
postgres `5433` · redis `6380` · kafka `9094` · mssql `1433` · Service Bus emulator `5672`.

---

## Test

```bash
dotnet test tests/BankingDomain.UnitTests          # 64 test — domain thuần, in-memory
dotnet test tests/BankingDomain.IntegrationTests   # 10 test — PostgreSQL + SQL Server thật
```

Integration test dùng Testcontainers để bắt đúng loại lỗi chỉ lộ ở tầng database: mapping
`DateTimeOffset` ↔ `timestamptz`, concurrency token chặn lost update, inbox khử trùng bằng khoá
chính, `FOR UPDATE SKIP LOCKED` cho outbox chạy nhiều instance. Không có Docker thì các test này
**tự bỏ qua** chứ không báo lỗi.

Trong đó có test hồi quy cho lỗi chi tiền hai lần: cùng một message id nhưng lệnh chuyển tiền khác
nhau, đúng như khi broker giao lại message thật.

---

## Hiệu năng

Đo trên máy phát triển, toàn bộ hạ tầng chạy Docker cùng một máy. Tái lập bằng
[`deploy/bench/run-bench.sh`](deploy/bench/run-bench.sh) — script tự seed 200.000 hợp đồng và
600.000 hồ sơ, đo, rồi dọn dữ liệu.

| | |
|---|---|
| Đường đọc | 1.602 req/giây · p95 58ms · 0 lỗi |
| Đường ghi | 364 req/giây · p95 131ms · 0 lỗi |
| Hàng đợi giám định | 461ms → **0,1ms** sau khi thay bằng partial index |
| Nhịp đẩy outbox | 8 → khoảng 1.600 sự kiện/giây sau khi gộp lô ở ranh giới mạng |

Sau 6.005 lệnh chuyển tiền liên tiếp: tổng số dư không đổi, tổng Nợ bằng tổng Có, mỗi lệnh đúng
một dòng inbox, không lệnh nào kẹt trạng thái.

**Hai điểm đáng chú ý khi tối ưu.** Composite index `(Status, CreatedAt)` bị PostgreSQL bỏ qua vì
điều kiện lọc khớp khoảng 40% bảng — phải dùng partial index mới có tác dụng. Và nút thắt outbox
không nằm ở chu kỳ poll như tưởng ban đầu, mà ở chỗ mỗi message tốn một lượt đi-về mạng.

---

## So sánh ba broker

| Tiêu chí | Kafka | Azure Service Bus | Azure Event Grid |
|---|---|---|---|
| Mô hình | Distributed log (partition) | Enterprise message broker | Event routing pub/sub |
| Phù hợp với | Streaming, throughput cao, replay | Lệnh nghiệp vụ cần chắc chắn, ordering | Thông báo sự kiện, serverless |
| Thứ tự | Theo partition | Theo session (FIFO) | Không đảm bảo |
| Retry / DLQ | Tự xử lý qua offset | Có sẵn dead-letter + MaxDeliveryCount | Retry + dead-letter ra Storage |
| Lưu trữ | Theo thời gian/dung lượng, đọc lại được | Tới khi consume xong | Không lưu, chỉ đẩy |
| Consumer | Pull (consumer group) | Pull (competing consumers) | Push (webhook) |

Trong dự án, Service Bus và Kafka chạy **song song theo hai vai trò khác nhau**, không phải chọn một:
saga đi qua Service Bus, còn audit trail dựng từ Kafka stream. Kafka lỗi thì luồng giao dịch vẫn
chạy bình thường vì stream là best-effort.

---

## Cấu trúc

```
src/
  BuildingBlocks.Contracts/   integration event — không phụ thuộc gói nào
  BuildingBlocks/             messaging · auth · state · observability
  Accounts/                   Domain → Application → Infrastructure → Api
  Payments/                   Domain → Application → Infrastructure → Api
  Insurance/                  Domain → Application → Infrastructure → Api
tests/                        unit + integration (Testcontainers)
frontend/                     React + Vite + TypeScript
deploy/                       k8s manifests · benchmark · cấu hình emulator
protos/                       định nghĩa gRPC
```

Mỗi service theo Clean Architecture bốn lớp, `Domain` không phụ thuộc gì.
`BuildingBlocks.Contracts` tách riêng khỏi `BuildingBlocks` để sửa hạ tầng không buộc build lại
toàn bộ service.

## Tài liệu

- [`deploy/k8s/README.md`](deploy/k8s/README.md) — manifest và cách deploy lên AKS
- [`frontend/README.md`](frontend/README.md) — banking console
- [`deploy/ci/ci.yml.reference`](deploy/ci/ci.yml.reference) — cấu hình GitHub Actions

## Giới hạn đã biết

Nói rõ để khỏi hiểu nhầm phạm vi của dự án:

- Chạy trên **Service Bus emulator**, chưa deploy lên Azure thật.
- Manifest Kubernetes mới kiểm tra cú pháp offline, chưa apply lên cụm thật.
- Số liệu hiệu năng đo trên một máy, hạ tầng single-node — dùng để so sánh trước/sau, không phải
  con số production.
- OpenTelemetry đã gắn nhưng chưa nối vào backend tracing.
