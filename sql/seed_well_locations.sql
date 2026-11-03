-- Идемпотентный сид координат устьев скважин Volve (WGS84).
-- Источник координат: WITSML-выгрузки Equinor, data/Volve_Well_technical_data/WellWellbore/...
-- Заголовки файлов содержат Surface Latitude/Longitude (DMS) и Surface EW/NS (UTM),
-- датум "European 1950 - Mean", Map-Zone "Zone 31N" (ED50 / UTM 31N).
-- Конвертация ED50 -> WGS84 выполнена по EPSG:1613 "ED50 to WGS 84 (24)"
-- (Position Vector, 7 параметров), рекомендуемому EPSG для норвежского шельфа
-- южнее 62°N (Северное море).
--
-- Запуск: psql ... -f sql/seed_well_locations.sql (можно повторять — апсерт по well_id).

INSERT INTO well_locations (well_id, latitude, longitude, source)
SELECT
    w.id,
    v.latitude,
    v.longitude,
    v.source
FROM (VALUES
    (5693, 58.44097404, 1.88589571, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-4/15_9-F-4/Volve F_F-4_F-4_F-4_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)'),
    (7405, 58.44102618, 1.88583679, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-1/15_9-F-1 C/Volve F_F-1_F-1 C_F-1 C_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)'),
    (5351, 58.44098776, 1.88593993, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-14/15_9-F-14/Volve F_F-14_F-14_F-14_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)'),
    (5599, 58.44102254, 1.88590065, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-12/15_9-F-12/Volve F_F-12_F-12_F-12_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)'),
    (5769, 58.44095665, 1.88591532, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-5/15_9-F-5/Volve F_F-5_F-5_F-5_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)'),
    (7078, 58.44103990, 1.88588101, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-11/15_9-F-11/Volve F_F-11_159-F-11_159-F-11_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)'),
    (7289, 58.44097037, 1.88595955, 'data/Volve_Well_technical_data/WellWellbore/15_9-F-15/15_9-F-15 D/Volve F_F-15_F-15D_F-15D_ACTUAL (ED50 UTM31N -> WGS84 EPSG:1613)')
) AS v(npd_well_bore_code, latitude, longitude, source)
JOIN wells w ON w.npd_well_bore_code = v.npd_well_bore_code
ON CONFLICT (well_id) DO UPDATE SET
    latitude = EXCLUDED.latitude,
    longitude = EXCLUDED.longitude,
    source = EXCLUDED.source;
