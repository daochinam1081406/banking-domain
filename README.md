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
- **Payments.Api** — nhận lệnh chuyển tiền → publish `MoneyTransferredIntegrationEvent` (MessageId = idempotency).
- **Accounts.Api** — consume qua subscription, cập nhật số dư; `MaxDeliveryCount=5` → dead-letter.

## Chạy local (không cần Azure thật)

```bash
docker compose up --build          # mssql → Service Bus emulator → 2 services
# Publish 1 giao dịch:
curl -X POST http://localhost:8081/api/transfers \
  -H "Content-Type: application/json" \
  -d '{"fromAccount":"ACC-001","toAccount":"ACC-002","amount":500000,"currency":"VND"}'
# Kiểm tra số dư (Accounts đã consume event):
curl http://localhost:8082/api/accounts/ACC-002
```

## Tech stack
.NET 9 · ASP.NET Core Minimal API · **Azure Service Bus** (`Azure.Messaging.ServiceBus`) ·
Docker Compose · Azure Service Bus **emulator** (chạy offline).

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

## Roadmap
- **Phase 1 (hiện tại):** 2 service + Service Bus pub/sub + emulator + README. ✅
- **Phase 2:** PostgreSQL per-service (EF Core + Dapper), Account/Transfer aggregate (DDD + Clean Architecture), **Outbox pattern**, JWT auth, **gRPC** (Payments → Accounts validate), **Azure Event Grid** cho `AccountOpened`.
- **Phase 3:** GitHub Actions CI/CD, Kubernetes manifests (AKS), Application Insights, Polly resilience.
