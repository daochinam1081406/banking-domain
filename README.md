# banking-domain — .NET Core Microservices (Event-Driven, Azure Service Bus)

Demo **microservices bám sát yêu cầu vị trí .NET Core Backend/Microservices**: .NET 9, kiến trúc
event-driven qua **Azure Service Bus**, mỗi service tách biệt, giao tiếp bất đồng bộ. Domain: ngân
hàng (Account / Payment) — pattern áp dụng 1:1 cho bảo hiểm (Policy ↔ Account, Claim ↔ Transfer).

> Người viết đã có sẵn nền event-driven ở quy mô lớn với **Kafka** (dự án ERP/MES D-Pro: 16 topics,
> Transactional Outbox, CQRS + Event Sourcing). Repo này tập trung chứng minh phần **Azure messaging**
> (Service Bus / Event Grid) — ánh xạ trực tiếp từ tư duy Kafka sang stack Azure.

## Kiến trúc (Phase 1 — walking skeleton)

```
POST /api/transfers                         GET /api/accounts/{id}
        │                                            ▲
        ▼                                            │
┌───────────────┐   MoneyTransferred     ┌────────────────────┐
│  Payments.Api │ ─────────────────────► │    Accounts.Api    │
│  (publisher)  │   Azure Service Bus    │ (BackgroundService │
└───────────────┘   topic: banking-events│  consumer)         │
                     sub: accounts-sub    └────────────────────┘
                     (dead-letter, retry)
```

- **BuildingBlocks** — `IEventBus` abstraction + `AzureServiceBusEventBus` impl + integration event contracts. Đổi broker (Kafka/Event Grid) chỉ thay implementation.
- **Payments.Api** — nhận lệnh chuyển tiền → gRPC validate → ghi Transfer + **Outbox** (1 transaction) → publish.
- **Accounts.Api** — consume, áp số dư **idempotent** (bảng inbox `processed_messages` + optimistic concurrency `RowVersion`), rồi phát `TransferCompleted`/`TransferFailed`.
- **Saga khép kín** — Payments consume event kết quả → transfer chuyển `Initiated → Completed | Failed` (không kẹt trạng thái).

## Chạy local (không cần Azure thật) — ✅ đã verify end-to-end

```bash
docker compose up -d --build       # mssql + postgres + redis + Service Bus emulator + 2 service

# 1) Lấy JWT (endpoint nghiệp vụ yêu cầu Bearer token)
TOKEN=$(curl -s -X POST http://localhost:8081/token -H "Content-Type: application/json" \
  -d '{"subject":"demo","role":"customer"}' | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 2) Chuyển tiền (Payments: gRPC validate số dư → Outbox → Service Bus)
curl -X POST http://localhost:8081/api/transfers -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"fromAccount":"ACC-001","toAccount":"ACC-002","amount":500000,"currency":"VND"}'

# 3) Kiểm tra số dư (Accounts đã consume event, cập nhật SQL Server)
curl http://localhost:8082/api/accounts/ACC-002 -H "Authorization: Bearer $TOKEN"
# ACC-001: 10,000,000 → 9,500,000 · ACC-002: 5,000,000 → 5,500,000  (seed sẵn ACC-001/002/003)
```

**Ports:** Payments REST `8081` · Accounts REST `8082` · Accounts gRPC `8090` (Http2 h2c, nội bộ) ·
postgres `5433` · redis `6380` · mssql `1433` · Service Bus emulator `5672`.

## Tech stack
.NET 9 · ASP.NET Core Minimal API · **SQL Server + EF Core** (Accounts) · **PostgreSQL + Dapper**
(Payments) · **Outbox pattern** · Messaging cắm được **Azure Service Bus / Azure Event Grid / Kafka**
qua 1 `IEventBus` · Docker Compose · Azure Service Bus **emulator** (chạy offline).

## 1 abstraction — 3 broker (đúng scope JD)

JD yêu cầu cả **Azure Service Bus + Azure Event Grid + Kafka**. Cả ba đều là implementation của
`IEventBus`; đổi broker chỉ bằng config, không đụng domain/application:

```jsonc
// appsettings / env — chọn provider
"Messaging": { "Provider": "ServiceBus" }   // hoặc "EventGrid" | "Kafka"
```

| Provider | Impl | Config section |
|----------|------|---------------|
| `ServiceBus` (mặc định) | `AzureServiceBusEventBus` | `ServiceBus` |
| `EventGrid` | `EventGridEventBus` | `EventGrid` (TopicEndpoint + AccessKey) |
| `Kafka` | `KafkaEventBus` | `Kafka` (BootstrapServers + Topic) |

## So sánh Kafka ↔ Azure Service Bus ↔ Event Grid

| Tiêu chí | **Kafka** | **Azure Service Bus** | **Azure Event Grid** |
|----------|-----------|----------------------|---------------------|
| Mô hình | Distributed log (partitioned) | Enterprise message broker | Event routing pub/sub |
| Use case | Streaming, event sourcing, throughput cao | Command/message tin cậy, ordering, transaction | Event notification, reactive/serverless |
| Ordering | Theo partition | Theo session (FIFO) | Không đảm bảo |
| Delivery | At-least-once (exactly-once qua idempotent producer + tx) | At-least-once, dedup theo MessageId | At-least-once |
| Retry/DLQ | Tự xử lý (consumer offset) | **Built-in** dead-letter + MaxDeliveryCount | Retry + dead-letter tới Storage |
| Retention | Cấu hình theo thời gian/size (replay được) | Tới khi consume (TTL) | Không lưu (chỉ đẩy) |
| Consumer | Pull (consumer group) | Pull (competing consumers) | Push (webhook/handler) |
| Khi nào chọn | Pipeline dữ liệu lớn, cần replay | Giao dịch nghiệp vụ cần chắc chắn, ordering | Phản ứng sự kiện rời rạc, tích hợp Azure |

**Tư duy chuyển đổi:** Kafka topic+partition+consumer group ≈ Service Bus topic+subscription+competing
consumers. Outbox pattern (đảm bảo publish sau khi commit DB) áp dụng chung cho cả hai.

## Frontend (React + Vite + TS)
Banking console (`frontend/`) — Login (JWT) · Dashboard số dư · Chuyển tiền (thể hiện event-driven
async). JD không yêu cầu FE; làm để tường minh luồng nghiệp vụ. Xem `frontend/README.md`.
```bash
cd frontend && npm install && npm run dev   # http://localhost:5174
```

## Test

```bash
dotnet test tests/BankingDomain.UnitTests          # 55 test — domain rules, thuần in-memory
dotnet test tests/BankingDomain.IntegrationTests   # 8 test — PostgreSQL + SQL Server THẬT (Testcontainers)
```

Integration test bắt đúng loại lỗi unit test không thấy: mapping `DateTimeOffset` ↔ `timestamptz`,
`RowVersion` chặn lost update, inbox dedup bằng PRIMARY KEY, `FOR UPDATE SKIP LOCKED`.
Không có Docker → test **tự skip** thay vì fail.

## Tài liệu
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — sơ đồ component + sequence (Mermaid), nguyên tắc thiết kế
- [`deploy/k8s/README.md`](deploy/k8s/README.md) — deploy AKS
- [`docs/MONOLITH-TO-MICROSERVICES.md`](docs/MONOLITH-TO-MICROSERVICES.md) — strangler fig, khi nào tách, trade-off, bài học thật
- `CLAUDE.md` / `.cursorrules` — brief cho AI assistant (quy tắc allinone)

## Roadmap — tất cả DONE ✅
- **Phase 1:** 2 service + Azure Service Bus pub/sub + emulator.
- **Phase 2:** PostgreSQL (EF Core + Dapper), DDD + Clean Architecture, **Outbox**, JWT, **gRPC**, **Event Grid + Kafka** (IEventBus), Redis.
- **Phase 3:** GitHub Actions CI + xUnit tests + Kubernetes manifests (AKS) + Application Insights.
- **Bonus:** React frontend. **Verified end-to-end** qua `docker compose`.
