# banking-domain — Project Brief for AI Assistants

> Đọc file này trước khi làm bất kỳ task nào. Repo này theo cùng bộ quy tắc "allinone" (D-Pro):
> conventions code, git, tài liệu đồng nhất.
>
> ⚠️ **QUY TẮC ĐỒNG BỘ (bắt buộc):** `.cursorrules` là bản mirror của file này để Cursor AI hỗ trợ song song.
> **Sửa CLAUDE.md thì PHẢI cập nhật `.cursorrules` trong cùng commit** (và ngược lại) — đặc biệt khi đổi:
> scope mapping, patterns/reliability rules, conventions, trạng thái. Lệch 2 file = Cursor sinh code sai context.

---

## 1. Mục tiêu

Demo **.NET Core microservices** bám sát **100% scope** một JD .NET Core Backend/Microservices
(insurance/banking domain). Domain: ngân hàng (Account / Payment) — pattern map 1:1 sang bảo hiểm
(Policy ↔ Account, Claim ↔ Transfer). Mục tiêu là **portfolio piece đóng đúng gap** mà dự án ERP/MES
D-Pro (composable monolith, Kafka) không thể hiện: **Azure messaging + microservices tách thật**.

---

## 2. Scope mapping (bám sát JD — cập nhật mỗi khi thêm feature)

| JD yêu cầu | Trạng thái | Ở đâu |
|-----------|-----------|-------|
| .NET Core / ASP.NET Core | ✅ | .NET 9 Minimal API |
| **SQL Server** (data modeling) | ✅ P2 | Accounts + EF Core + migration |
| **PostgreSQL** (query optimization) | ✅ P2 | Payments + Dapper raw SQL |
| **Outbox pattern** | ✅ P2 | Payments: transfer + outbox 1 tx → publisher |
| Redis | ✅ P2 | cache-aside số dư (Accounts) + invalidate khi transfer |
| **Azure Service Bus** | ✅ P1 | `AzureServiceBusEventBus` |
| **Azure Event Grid** | ✅ P2 | `EventGridEventBus` (chọn qua `Messaging:Provider`) |
| **Kafka** | ✅ P2 | `KafkaEventBus` (chọn qua `Messaging:Provider`) |
| Microservices architecture | ✅ | 2 service tách, **DB-per-service** (SQL Server + Postgres) |
| Event-driven architecture | ✅ | integration events qua broker + Outbox |
| RESTful API + **gRPC** | ✅ P2 | Minimal API + gRPC AccountCheck (Payments→Accounts sync) |
| Clean Architecture + DDD | ✅ P2 | Domain/Application/Infra/Api, aggregate |
| OAuth2 / OIDC / JWT | ✅ P2+ | JWT bearer + **refresh token rotation & reuse detection** (`/auth/login\|refresh\|logout`), chỉ lưu SHA-256 hash |
| Observability | ✅ | **Correlation ID** xuyên HTTP→gRPC→outbox→message→consumer · Serilog structured · OpenTelemetry tracing |
| Resilience | ✅ | Polly `AddStandardResilienceHandler` trên gRPC client (retry + circuit breaker + timeout) |
| Docker | ✅ | Dockerfile + compose |
| CI/CD, Kubernetes, App Insights | ✅ P3 | GitHub Actions CI + k8s/AKS manifests + App Insights (conditional) |
| Unit tests | ✅ P3 | xUnit 12 tests (Account overdraft, Transfer rules) |
| Frontend (bonus — JD không yêu cầu) | ✅ | React+Vite+TS `frontend/` — Login (JWT), Dashboard số dư, Chuyển tiền (event-driven async) |
| Docs | ✅ | ARCHITECTURE.md (Mermaid), k8s README, frontend README, CLAUDE.md + .cursorrules |

**Nguyên tắc:** mỗi feature mới PHẢI ánh xạ về 1 dòng JD. Không thêm thứ ngoài scope.

---

## 3. Kiến trúc

- **Event-driven microservices**, mỗi service **database-per-service** (không chia sẻ DB).
- Giao tiếp **bất đồng bộ** qua message broker sau `IEventBus` abstraction — cắm được
  **Azure Service Bus · Azure Event Grid · Kafka** mà không đụng domain code.
- Giao tiếp **đồng bộ** (khi cần) qua **gRPC** (Payments → Accounts validate).
- **Outbox pattern**: persist aggregate + outbox trong 1 transaction, publisher đọc outbox → broker
  (đảm bảo không mất event khi DB commit nhưng broker lỗi).

```
POST /api/transfers → Payments (PostgreSQL/Dapper) → [Outbox] → IEventBus
   → MoneyTransferred → Azure Service Bus topic/subscription
   → Accounts consumer (SQL Server/EF Core) → cập nhật số dư → GET /api/accounts/{id}
```

---

## 4. Tech stack

| Layer | Tech |
|-------|------|
| Language/API | C# .NET 9, ASP.NET Core Minimal API |
| Databases | **SQL Server** (Accounts, EF Core) · **PostgreSQL** (Payments, Dapper) |
| Cache | Redis |
| Messaging | Azure Service Bus · Azure Event Grid · Kafka (qua `IEventBus`) |
| Sync RPC | gRPC |
| Auth | JWT bearer (OAuth2/OIDC) |
| Container | Docker multi-stage + docker-compose (kèm Azure Service Bus emulator) |
| CI/CD | GitHub Actions · k8s manifests (AKS) |
| Serialization | System.Text.Json |

---

## 5. Cấu trúc thư mục

```
banking-domain/
├── BankingDomain.sln
├── src/
│   ├── BuildingBlocks/           ← IEventBus, IntegrationEvent, broker impls, contracts (dùng chung)
│   ├── Accounts/
│   │   ├── Accounts.Domain/       ← Account aggregate, VO, domain events, exceptions (KHÔNG phụ thuộc gì)
│   │   ├── Accounts.Application/  ← commands/queries + handlers + repo interfaces
│   │   ├── Accounts.Infrastructure/ ← EF Core (SQL Server), Outbox, consumers
│   │   └── Accounts.Api/          ← Minimal API endpoints
│   └── Payments/
│       ├── Payments.Domain/
│       ├── Payments.Application/
│       ├── Payments.Infrastructure/ ← Dapper (PostgreSQL), Outbox, publisher
│       └── Payments.Api/
├── deploy/                        ← servicebus-emulator-config.json, k8s manifests
├── docker-compose.yml
└── README.md
```

> Phase 1 (hiện tại) gộp Domain/Application/Infra trong `*.Api` để walking skeleton chạy trước.
> Phase 2 tách đủ 4 layer Clean Architecture như trên.

---

## 6. Core patterns (bắt buộc follow)

### 6.1 Integration event (BuildingBlocks)
```csharp
public sealed record MoneyTransferredIntegrationEvent : IntegrationEvent
{
    public required Guid TransferId { get; init; }
    public required string FromAccount { get; init; }
    // ...
}
```

### 6.2 IEventBus — 1 abstraction, nhiều broker
```csharp
public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IntegrationEvent;
}
// impl: AzureServiceBusEventBus | EventGridEventBus | KafkaEventBus
// MessageId = EventId → idempotency; Subject = EventType → subscription routing
```

### 6.3 Consumer (Infrastructure) — BackgroundService
- `AutoCompleteMessages = false`; xử lý xong → `CompleteMessageAsync`; lỗi → `AbandonMessageAsync`
  (retry); quá `MaxDeliveryCount` → dead-letter.
- Handler **idempotent** (check MessageId / natural key).

### 6.4 Aggregate (DDD, Domain layer) — Phase 2
- `sealed class : AggregateRoot`, `private ctor`, factory `Create(...)` + `Reconstitute(...)`.
- Strongly-typed ID (record), Value Objects validate trong constructor.
- Domain exception kế thừa `Exception` + `string ErrorCode`.

### 6.5 Reliability (BẮT BUỘC khi thêm consumer mới)
- **Inbox/dedup**: mọi consumer phải check `IInboxStore.AlreadyProcessedAsync(messageId)` trước khi
  áp side-effect; `MarkProcessed` commit **cùng transaction** với thay đổi dữ liệu. Broker chỉ
  at-least-once → không có inbox = trừ tiền 2 lần.
- **Optimistic concurrency**: entity bị sửa song song phải có `RowVersion`; retry khi `DbUpdateConcurrencyException`.
- **Phân loại lỗi**: lỗi nghiệp vụ (permanent) → phát compensating event + Complete message; lỗi hạ tầng
  (transient) → Abandon để retry; payload hỏng → DeadLetter thẳng.
- **Saga**: mọi lệnh phải có trạng thái cuối (Completed/Failed), không để kẹt ở trạng thái khởi tạo.

### 6.6 Outbox — Phase 2
- Persist aggregate + outbox row trong **1 transaction**; publisher (BackgroundService) poll outbox → `IEventBus`.

---

## 7. Code conventions (giống allinone / D-Pro)

1. `sealed` cho mọi class và record cụ thể.
2. **Primary constructor injection** (C# 12): `class Foo(IBar bar)`.
3. `async Task<T>` cho mọi I/O, `CancellationToken ct` bắt buộc.
4. **EF Core** cho Accounts (SQL Server) · **Dapper + raw SQL** cho Payments (PostgreSQL).
5. Không comment WHAT — chỉ comment WHY khi không rõ.
6. Validate ở boundary (API / VO constructor), không validate lại ở handler.
7. Endpoint inject handler (`ICommandHandler<,>`), không inject repository trực tiếp (Phase 2).
8. PascalCase cho public members; DTO record positional khớp số cột SELECT (Dapper).

---

## 8. Thêm microservice mới (step-by-step)

1. `dotnet new` 4 project: `X.Domain`, `X.Application`, `X.Infrastructure`, `X.Api` + add vào sln.
2. Reference: Api→Application+Infrastructure, Infrastructure→Application, Application→Domain, tất cả→BuildingBlocks.
3. Domain: aggregate + events + VO + exception. Application: commands/queries + repo interface.
4. Infrastructure: EF Core **hoặc** Dapper repo + Outbox + consumer (đăng ký `IEventBus`).
5. Api: Minimal API endpoints + DI.
6. Contract integration event mới → đặt trong `BuildingBlocks/Contracts`.
7. docker-compose: thêm DB + service; cập nhật scope table ở §2 + README.

---

## 9. Git conventions — CỰC KỲ QUAN TRỌNG (giống allinone)

```bash
# ✅ đúng — conventional commits
git commit -m "feat: add PostgreSQL Dapper repo for Payments"
git commit -m "fix: idempotent MoneyTransferred consumer"
git commit -m "docs: update scope mapping"

# ❌ TUYỆT ĐỐI KHÔNG thêm dòng Co-Authored-By vào commit
# ❌ KHÔNG commit secrets / connection string thật / token — dùng env var + user-secrets
```
- **Branch flow (giống allinone/D-Pro):** `namdaoint` (dev chính, default) → `develop` → `staging` → `production`.
  Làm việc trên `namdaoint`, PR/merge lên develop → staging → production.
- Prefix conventional: `feat: fix: docs: refactor: test: chore:`.
- Token/PAT: KHÔNG lưu vào `.git/config` hay file; push dùng token inline rồi revoke.

---

## 10. Run commands

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build BankingDomain.sln
dotnet test                       # Phase 2 khi có test
docker compose up --build         # mssql → Service Bus emulator → payments + accounts

# Smoke test luồng event:
curl -X POST http://localhost:8081/api/transfers \
  -H "Content-Type: application/json" \
  -d '{"fromAccount":"ACC-001","toAccount":"ACC-002","amount":500000,"currency":"VND"}'
curl http://localhost:8082/api/accounts/ACC-002
```

---

## 11. Roadmap

- **Phase 1 (done):** 2 service + Azure Service Bus pub/sub + emulator + README so sánh Kafka↔Azure.
- **Phase 2:** SQL Server (Accounts/EF) + PostgreSQL (Payments/Dapper) + DDD/Clean Arch + Outbox + JWT + gRPC + Azure Event Grid + Kafka impl + Redis.
- **Phase 3 (done):** GitHub Actions CI (build+test) + xUnit tests + k8s/AKS manifests + Application Insights (conditional). Verified e2e qua docker compose.
