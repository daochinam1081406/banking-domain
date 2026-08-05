-- Dữ liệu benchmark cho Insurance: 200k hợp đồng / 600k hồ sơ bồi thường
-- (quy mô một công ty bảo hiểm cỡ vừa sau vài năm hoạt động).
--
-- Mục đích: đo query trên dữ liệu ĐỦ LỚN. Trên bảng vài chục dòng thì Seq Scan cũng
-- nhanh, mọi index đều "có vẻ ổn" và không thể phân biệt thiết kế tốt với thiết kế tồi.
--
--   docker compose exec -T postgres psql -U postgres -d insurance < deploy/bench/seed-bench-data.sql
--
-- Xoá sau khi đo: deploy/bench/clean-bench-data.sql

INSERT INTO policies ("Id","PolicyNumber","PolicyHolderId","ProductCode","CoverageAmount","PremiumAmount",
  "Deductible","CoPaymentRate","WaitingPeriodDays","Currency","PayoutAccount","Status",
  "EffectiveFrom","EffectiveTo","ClaimedAmount","UpdatedAt")
SELECT gen_random_uuid(), 'BENCH-POL-'||lpad(g::text,8,'0'), 'bench-holder-'||(g%20000),
       (ARRAY['HEALTH','MOTOR','TRAVEL'])[1+g%3], 100000000, 2000000, 500000, 0.20, 30, 'VND',
       'ACC-'||(g%20000), (ARRAY['Active','Draft','Expired'])[1+g%3],
       DATE '2025-01-01', DATE '2027-01-01', 0,
       NOW() - (g % 500000) * INTERVAL '1 minute'
FROM generate_series(1,200000) g;

-- Trạng thái rải đều 5 giá trị ⇒ nhóm "đang chờ xử lý" (Submitted + UnderReview) chiếm ~40% bảng.
-- Tỷ lệ này quan trọng: nó chính là lý do index thường bị planner bỏ qua (xem docs/PERFORMANCE.md).
INSERT INTO claims ("Id","ClaimNumber","PolicyId","PolicyNumber","ClaimantId","RequestedAmount",
  "Currency","IncidentDate","Description","Status","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(), 'BENCH-CLM-'||lpad(g::text,8,'0'),
       gen_random_uuid(), 'BENCH-POL-'||lpad((1+g%200000)::text,8,'0'), 'bench-holder-'||(g%20000), 5000000,
       'VND', DATE '2026-01-15', 'Ho so benchmark '||g,
       (ARRAY['Submitted','UnderReview','Approved','Paid','Rejected'])[1+g%5],
       NOW() - (g % 500000) * INTERVAL '1 minute',
       NOW() - (g % 500000) * INTERVAL '1 minute'
FROM generate_series(1,600000) g;

-- Không ANALYZE thì planner còn dùng thống kê cũ và chọn sai kế hoạch — số đo sẽ vô nghĩa.
ANALYZE policies;
ANALYZE claims;

SELECT (SELECT count(*) FROM policies) AS policies, (SELECT count(*) FROM claims) AS claims;
