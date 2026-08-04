# banking-domain — Frontend (React + Vite + TypeScript)

Banking console gọi 2 microservice. JD không yêu cầu FE — làm để tường minh luồng nghiệp vụ +
minh hoạ kiến trúc (giống cách D-Pro có FR frontend).

## Màn hình (đúng mindset banking)
- **Login** — lấy JWT qua Payments `/token` (subject = tên user).
- **Dashboard** — tài khoản **của chính mình** (lọc theo JWT subject) + mở tài khoản mới (VND/USD).
  Bấm vào card → **Sao kê**: bảng bút toán kép (Ghi nợ/Ghi có, số dư sau, mã giao dịch).
- **Chuyển tiền** — form transfer → Payments (gRPC validate → Outbox → Service Bus); số dư cập nhật
  **bất đồng bộ** (thể hiện đúng event-driven — bấm "Xem số dư" sau vài giây).

## Chạy
```bash
cd frontend
npm install
npm run dev      # http://localhost:5174
```
Cần 2 API chạy (docker compose up ở repo gốc). Base URL cấu hình qua env:
```
VITE_PAYMENTS_URL=http://localhost:8081   # mặc định
VITE_ACCOUNTS_URL=http://localhost:8082
```

## Cấu trúc
`src/api` (client + banking calls) · `src/auth` (AuthContext + RequireAuth, JWT trong localStorage) ·
`src/layout` (AppShell) · `src/pages` (Login, Dashboard, Transfers). Stack: React 18 · Vite 5 · TS ·
react-router. JWT gắn tự động vào header `Authorization` cho mọi request.
