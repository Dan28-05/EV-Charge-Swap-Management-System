-- Optional demo data. Run only in a local development database after applying migrations.
INSERT INTO locations
    (id, name, address, latitude, longitude, offers_car_charging, offers_battery_swap, is_active, created_at_utc)
VALUES
    ('11111111-1111-4111-8111-111111111111', 'Demo Station A', 'Sample address, District 1', 10.776889, 106.700806, true, true, true, now()),
    ('22222222-2222-4222-8222-222222222222', 'Demo Swap Point B', 'Sample address, District 3', 10.782951, 106.686274, false, true, true, now())
ON CONFLICT (id) DO NOTHING;
