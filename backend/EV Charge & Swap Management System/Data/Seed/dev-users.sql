-- Fictional demo users for a local development database only.
-- All users are pre-verified and active with known test passwords.
-- Compatible with ASP.NET Core Identity PasswordHasher.
-- Stable IDs and ON CONFLICT keep this script safe to run more than once.

-- 1. Quản trị viên hệ thống (Admin)
-- Email: admin@evcharge.vn | Mật khẩu: Admin@123456 | SĐT: +84901234567
INSERT INTO users (
    id, full_name, is_active, created_at_utc,
    user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp,
    phone_number, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count
) VALUES (
    'a1111111-1111-4111-8111-111111111111', 'Quản Trị Viên Hệ Thống', true, now(),
    'admin@evcharge.vn', 'ADMIN@EVCHARGE.VN', 'admin@evcharge.vn', 'ADMIN@EVCHARGE.VN',
    true, 'AQAAAAIAAYagAAAAEPaGyb0mCo7GNxC0VnBi0AMRBPlo2nyUAtnKEKkjyBPERdiJF7vhQDZwlQVgYyvKCA==', 'EC0C9D94AC0B4EED8D209ED17335696F', '82127a6a-f55b-4cc0-8acc-7a138123c06f',
    '+84901234567', true, false, true, 0
)
ON CONFLICT (id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    password_hash = EXCLUDED.password_hash,
    security_stamp = EXCLUDED.security_stamp,
    email_confirmed = true,
    phone_number_confirmed = true,
    is_active = true;

INSERT INTO user_roles (user_id, role_id)
VALUES ('a1111111-1111-4111-8111-111111111111', '345a3035-4f38-414a-bd80-33c820eb286b')
ON CONFLICT DO NOTHING;

-- 2. Nhân viên vận hành trạm (Operator)
-- Email: operator@evcharge.vn | Mật khẩu: Operator@123456 | SĐT: +84912345678
INSERT INTO users (
    id, full_name, is_active, created_at_utc,
    user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp,
    phone_number, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count
) VALUES (
    'b2222222-2222-4222-8222-222222222222', 'Nguyễn Văn Vận Hành', true, now(),
    'operator@evcharge.vn', 'OPERATOR@EVCHARGE.VN', 'operator@evcharge.vn', 'OPERATOR@EVCHARGE.VN',
    true, 'AQAAAAIAAYagAAAAEHgPudJcA3nRaFfEVuM7w5MIlsG6xJDiSRgWzYVUL332ytVA4t489eQW8lJgIxOVnw==', '955BCE03265D406B8B1CCF387C0F417C', '7b022e70-1b35-4bec-9f0c-01288df8cb6c',
    '+84912345678', true, false, true, 0
)
ON CONFLICT (id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    password_hash = EXCLUDED.password_hash,
    security_stamp = EXCLUDED.security_stamp,
    email_confirmed = true,
    phone_number_confirmed = true,
    is_active = true;

INSERT INTO user_roles (user_id, role_id)
VALUES ('b2222222-2222-4222-8222-222222222222', 'a21b7fc8-7c15-4548-b4d1-54d9af0e2739')
ON CONFLICT DO NOTHING;

-- 3. Khách hàng sử dụng dịch vụ (Customer 1)
-- Email: customer@evcharge.vn | Mật khẩu: Customer@123456 | SĐT: +84987654321
INSERT INTO users (
    id, full_name, is_active, created_at_utc,
    user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp,
    phone_number, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count
) VALUES (
    'c3333333-3333-4333-8333-333333333333', 'Trần Khách Hàng', true, now(),
    'customer@evcharge.vn', 'CUSTOMER@EVCHARGE.VN', 'customer@evcharge.vn', 'CUSTOMER@EVCHARGE.VN',
    true, 'AQAAAAIAAYagAAAAEGX5pzKM+Qz5SRKLbLFo7nfxQBCw++z1KiC9+RhwEDprlAYIwaaf/yAHSqdMK72jOA==', '8A81A49391124B8DBF6A5EAF01F93703', '6c3ab1d9-b016-4df9-bb20-9970388767c4',
    '+84987654321', true, false, true, 0
)
ON CONFLICT (id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    password_hash = EXCLUDED.password_hash,
    security_stamp = EXCLUDED.security_stamp,
    email_confirmed = true,
    phone_number_confirmed = true,
    is_active = true;

INSERT INTO user_roles (user_id, role_id)
VALUES ('c3333333-3333-4333-8333-333333333333', 'a56e1a51-b86d-4a39-99f0-22ee86bd65ef')
ON CONFLICT DO NOTHING;

-- 4. Tài xế lái xe điện (Customer 2 / Driver)
-- Email: driver@evcharge.vn | Mật khẩu: Driver@123456 | SĐT: +84978123456
INSERT INTO users (
    id, full_name, is_active, created_at_utc,
    user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp,
    phone_number, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count
) VALUES (
    'd4444444-4444-4444-8444-444444444444', 'Lê Tài Xế EV', true, now(),
    'driver@evcharge.vn', 'DRIVER@EVCHARGE.VN', 'driver@evcharge.vn', 'DRIVER@EVCHARGE.VN',
    true, 'AQAAAAIAAYagAAAAEIzylzAYEbw7k/yow+bF47verMy8VDdLSAgAKLAoX1A5mgMGMt5Ov+XjKOHs14sp4g==', 'E281E54533E748EBA5387C07563CD177', '3443ca57-1b89-4161-8eea-b57750443ad6',
    '+84978123456', true, false, true, 0
)
ON CONFLICT (id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    password_hash = EXCLUDED.password_hash,
    security_stamp = EXCLUDED.security_stamp,
    email_confirmed = true,
    phone_number_confirmed = true,
    is_active = true;

INSERT INTO user_roles (user_id, role_id)
VALUES ('d4444444-4444-4444-8444-444444444444', 'a56e1a51-b86d-4a39-99f0-22ee86bd65ef')
ON CONFLICT DO NOTHING;
