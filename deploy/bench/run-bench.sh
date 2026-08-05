#!/usr/bin/env bash
# Chạy lại toàn bộ số đo trong docs/PERFORMANCE.md.
#
#   ./deploy/bench/run-bench.sh          # đo query + load test
#   ./deploy/bench/run-bench.sh --clean  # xoá dữ liệu benchmark rồi thoát
#
# Yêu cầu: stack đang chạy (`docker compose up -d`) và có `ab` (Apache Bench).
set -euo pipefail
cd "$(dirname "$0")/../.."

PSQL_INS="docker compose exec -T postgres psql -U postgres -d insurance"
PSQL_PAY="docker compose exec -T postgres psql -U postgres -d payments"

if [[ "${1:-}" == "--clean" ]]; then
  $PSQL_INS < deploy/bench/clean-bench-data.sql
  exit 0
fi

command -v ab >/dev/null || { echo "Thiếu 'ab' (Apache Bench)."; exit 1; }

echo "═══ 1/4 · Seed dữ liệu benchmark (200k hợp đồng / 600k hồ sơ) ═══"
$PSQL_INS < deploy/bench/seed-bench-data.sql

echo
echo "═══ 2/4 · Hàng đợi giám định: kế hoạch thực thi ═══"
# Cùng một câu truy vấn, khác nhau ở chỗ có dùng được partial index hay không.
$PSQL_INS -c 'SET enable_indexscan = off; SET enable_bitmapscan = off;
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM claims
  WHERE "Status" IN (''Submitted'',''UnderReview'') ORDER BY "CreatedAt" DESC LIMIT 50;' \
  | grep -E "Seq Scan|Sort Method|Execution Time" | sed 's/^/  [KHÔNG index] /'

$PSQL_INS -c 'EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM claims
  WHERE "Status" IN (''Submitted'',''UnderReview'') ORDER BY "CreatedAt" DESC LIMIT 50;' \
  | grep -E "Index Scan|Sort|Execution Time" | sed 's/^/  [CÓ partial index] /'

echo
echo "═══ 3/4 · Load test đường đọc: GET /api/claims ═══"
TOK=$(curl -s -X POST http://localhost:8081/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"adjuster","password":"Adjuster@123"}' | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
ab -n 4000 -c 50 -k -H "Authorization: Bearer $TOK" http://localhost:8083/api/claims 2>&1 \
  | grep -E "Requests per second|Failed requests|^  95%|^  99%" | sed 's/^/  /'

echo
echo "═══ 4/4 · Load test đường ghi + độ trễ outbox: POST /api/transfers ═══"
CTOK=$(curl -s -X POST http://localhost:8081/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"demo","password":"Demo@123"}' | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
TMP=$(mktemp)
echo '{"fromAccount":"ACC-001","toAccount":"ACC-002","amount":10,"currency":"VND"}' > "$TMP"

until [ "$($PSQL_PAY -At -c "SELECT count(*) FROM outbox WHERE status='PENDING';")" = "0" ]; do sleep 2; done
START=$(date +%s)
ab -n 1000 -c 25 -k -T 'application/json' -p "$TMP" -H "Authorization: Bearer $CTOK" \
   http://localhost:8081/api/transfers 2>&1 \
  | grep -E "Requests per second|Failed requests|^  95%|^  99%" | sed 's/^/  /'
rm -f "$TMP"

while [ "$($PSQL_PAY -At -c "SELECT count(*) FROM outbox WHERE status='PENDING';")" != "0" ]; do sleep 1; done
echo "  outbox drain sạch sau $(( $(date +%s) - START ))s"

echo
echo "═══ Tính đúng đắn: tiền phải bảo toàn, sổ cái phải cân ═══"
docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Str0ng!Passw0rd' -C -d accounts -h -1 -W -Q "
SET NOCOUNT ON;
SELECT '  tong No - tong Co = ' + CAST(SUM(CASE WHEN Direction='Debit' THEN Amount ELSE -Amount END) AS VARCHAR) + '  (phai = 0)' FROM ledger_entries;
SELECT '  transferId khong co dung 2 but toan = ' + CAST(COUNT(*) AS VARCHAR) + '  (phai = 0)'
FROM (SELECT TransferId FROM ledger_entries GROUP BY TransferId HAVING COUNT(*) <> 2) x;" 2>&1 | grep -v '^$'

echo
echo "Xong. Dọn dữ liệu benchmark: ./deploy/bench/run-bench.sh --clean"
