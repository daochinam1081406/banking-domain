# Architecture — banking-domain

Microservices .NET 9 event-driven, database-per-service, messaging trừu tượng hoá (Service Bus /
Event Grid / Kafka), Outbox pattern, gRPC sync, JWT, Redis cache, React frontend.

## Component diagram

```mermaid
flowchart LR
  FE["React FE<br/>(Vite + TS)"] -->|REST + JWT| PAY["Payments.Api"]
  FE -->|REST + JWT| ACC["Accounts.Api"]

  PAY -->|"gRPC (h2c) validate số dư"| ACC
  PAY -->|"Transfer + Outbox<br/>(1 transaction)"| PG[("PostgreSQL<br/>Dapper")]
  PAY -.->|OutboxPublisher poll| SB{{"Azure Service Bus<br/>topic: banking-events"}}
  SB -->|"MoneyTransferred"| ACC
  ACC -->|"EF Core"| MSSQL[("SQL Server")]
  ACC -->|"cache-aside + invalidate"| REDIS[("Redis")]

  subgraph "IEventBus (1 abstraction, 3 broker)"
    SB
    EG{{"Azure Event Grid"}}
    KAFKA{{"Kafka"}}
  end
```

## Money transfer — sequence

```mermaid
sequenceDiagram
  actor U as User (FE)
  participant P as Payments.Api
  participant A as Accounts.Api
  participant DB as PostgreSQL (outbox)
  participant SB as Service Bus
  participant SQL as SQL Server

  U->>P: POST /api/transfers (JWT)
  P->>A: gRPC Check(fromAccount)
  A-->>P: exists + balance
  P->>DB: INSERT transfer + outbox (1 tx)
  P-->>U: 202 Accepted (Initiated)
  loop OutboxPublisher (1s, SKIP LOCKED)
    DB->>SB: publish MoneyTransferred
  end
  SB->>A: MoneyTransferred (subscription)
  A->>SQL: debit(from) + credit(to)
  A->>A: invalidate Redis cache
```

## Nguyên tắc thiết kế

| Vấn đề | Giải pháp |
|--------|-----------|
| Không mất event khi DB commit nhưng broker lỗi | **Outbox pattern** (persist + outbox 1 transaction → publisher → at-least-once) |
| Đổi message broker không sửa domain | **IEventBus** abstraction — Service Bus / Event Grid / Kafka qua `Messaging:Provider` |
| Consistency giữa 2 service | Eventual consistency + idempotent consumer (MessageId), dead-letter theo MaxDeliveryCount |
| Dependency chưa ready khi startup | Retry connection có backoff (portable, chạy cả k8s) |
| Multi-instance publisher | `FOR UPDATE SKIP LOCKED` trên outbox |
| Data modeling vs query optimization | EF Core (Accounts/SQL Server) + Dapper raw SQL (Payments/PostgreSQL) |

## Layers (Clean Architecture, mỗi service)
`Domain` (aggregate, không phụ thuộc) → `Application` (use case, interface) → `Infrastructure`
(EF/Dapper, Outbox, consumer, gRPC) → `Api` (Minimal API + gRPC). Shared: `BuildingBlocks` (IEventBus, contracts).
