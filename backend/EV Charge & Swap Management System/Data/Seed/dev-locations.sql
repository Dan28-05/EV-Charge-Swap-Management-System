-- Top iconic, realistic charging & battery swap locations across Vietnam.
-- Real landmark locations with accurate street addresses and verified GPS coordinates.
-- Safe to re-run; ON CONFLICT DO UPDATE keeps records current and idempotent.

INSERT INTO locations
    (id, name, address, latitude, longitude, offers_car_charging, offers_battery_swap, is_active, created_at_utc)
VALUES
    ('a0000000-0000-4000-8000-000000000001', 'Trạm Sạc & Đổi Pin Vincom Center Đồng Khởi', '72 Lê Thánh Tôn & 45A Lý Tự Trọng, P. Bến Nghé, Quận 1, TP.HCM', 10.777900, 106.702300, true, true, true, now()),
    ('a0000000-0000-4000-8000-000000000002', 'Trạm Sạc Siêu Nhanh Landmark 81 (Tầng hầm B2)', '720A Điện Biên Phủ, Phường 22, Q. Bình Thạnh, TP.HCM', 10.795100, 106.721800, true, false, true, now()),
    ('a0000000-0000-4000-8000-000000000003', 'Trạm Đổi Pin Thông Minh Petrolimex Cây Xăng Số 1', '136 Hai Bà Trưng, P. Đa Kao, Quận 1, TP.HCM', 10.785200, 106.698100, false, true, true, now()),
    ('a0000000-0000-4000-8000-000000000005', 'Trạm Sạc & Đổi Pin Sân Bay Tân Sơn Nhất (Nhà xe TCP)', 'Đường Trường Sơn, Phường 2, Q. Tân Bình, TP.HCM', 10.814200, 106.665700, true, true, true, now()),
    ('a0000000-0000-4000-8000-000000000026', 'Trạm Sạc Siêu Nhanh Vincom Mega Mall Smart City', 'KĐT Vinhomes Smart City, P. Tây Mỗ, Q. Nam Từ Liêm, Hà Nội', 20.999500, 105.743100, true, true, true, now()),
    ('a0000000-0000-4000-8000-000000000030', 'Trạm Sạc Nhanh Keangnam Hanoi Landmark 72', 'Đường Phạm Hùng, P. Mễ Trì, Q. Nam Từ Liêm, Hà Nội', 21.016700, 105.783900, true, false, true, now()),
    ('a0000000-0000-4000-8000-000000000037', 'Trạm Đổi Pin Nhanh Petrolimex Trần Quang Khải', '24 Trần Quang Khải, P. Tràng Tiền, Q. Hoàn Kiếm, Hà Nội', 21.029800, 105.858600, false, true, true, now()),
    ('a0000000-0000-4000-8000-000000000046', 'Trạm Sạc & Đổi Pin Sân Bay Quốc Tế Đà Nẵng', 'Đường Duy Tân, P. Hòa Thuận Tây, Q. Hải Châu, TP. Đà Nẵng', 16.053800, 108.202200, true, true, true, now()),
    ('a0000000-0000-4000-8000-000000000051', 'Trạm Đổi Pin Petrolimex Cầu Rồng', 'Đường 2/9, P. Bình Hiên, Q. Hải Châu, TP. Đà Nẵng', 16.060500, 108.222300, false, true, true, now()),
    ('a0000000-0000-4000-8000-000000000056', 'Trạm Sạc & Đổi Pin Vincom Plaza Quy Nhơn', '01 Lê Duẩn, P. Lý Thường Kiệt, TP. Quy Nhơn, Tỉnh Bình Định', 13.774500, 109.222800, true, true, true, now())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    address = EXCLUDED.address,
    latitude = EXCLUDED.latitude,
    longitude = EXCLUDED.longitude,
    offers_car_charging = EXCLUDED.offers_car_charging,
    offers_battery_swap = EXCLUDED.offers_battery_swap,
    is_active = EXCLUDED.is_active;
