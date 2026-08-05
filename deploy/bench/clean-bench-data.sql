-- Xoá dữ liệu benchmark, giữ nguyên dữ liệu demo thật.
-- Nhận diện theo tiền tố BENCH- nên không đụng hợp đồng/hồ sơ do ứng dụng tạo.
--
--   docker compose exec -T postgres psql -U postgres -d insurance < deploy/bench/clean-bench-data.sql

DELETE FROM claims   WHERE "ClaimNumber"  LIKE 'BENCH-%';
DELETE FROM policies WHERE "PolicyNumber" LIKE 'BENCH-%';

ANALYZE policies;
ANALYZE claims;

SELECT (SELECT count(*) FROM policies) AS policies_con_lai,
       (SELECT count(*) FROM claims)   AS claims_con_lai;
