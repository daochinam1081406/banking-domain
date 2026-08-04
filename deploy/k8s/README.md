# Kubernetes (AKS) manifests

Deploy 2 microservice lên AKS. **Deps là managed Azure services** (không chạy DB/broker trong cluster):
Azure SQL (Accounts) · Azure Database for PostgreSQL (Payments) · Azure Cache for Redis ·
Azure Service Bus. Kết nối qua `banking-secrets` (production: Azure Key Vault + CSI Secrets Store driver).

## Deploy
```bash
# 1. Build + push image lên ACR
az acr build -r <acr> -t banking-domain-accounts-api:latest -f src/Accounts/Accounts.Api/Dockerfile .
az acr build -r <acr> -t banking-domain-payments-api:latest -f src/Payments/Payments.Api/Dockerfile .

# 2. Điền secrets thật vào config.yaml (hoặc Key Vault), thay <acr> trong accounts.yaml/payments.yaml

# 3. Apply
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/config.yaml
kubectl apply -f deploy/k8s/accounts.yaml
kubectl apply -f deploy/k8s/payments.yaml

kubectl get pods -n banking
kubectl get svc  -n banking     # payments-svc EXTERNAL-IP = entry point
```

## Ghi chú
- **accounts-svc** ClusterIP (nội bộ) expose REST 8080 + gRPC 8090 (Http2 h2c) cho Payments gọi.
- **payments-svc** LoadBalancer — entry point REST public.
- readiness/liveness probe trên `/health`; 2 replica mỗi service.
- Scale: `kubectl scale deploy/payments-api --replicas=N -n banking` (OutboxPublisher an toàn multi-instance
  nhờ `FOR UPDATE SKIP LOCKED`; consumer competing-consumers qua Service Bus subscription).
