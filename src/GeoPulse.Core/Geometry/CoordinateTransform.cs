namespace GeoPulse.Core.Geometry
{
    /// <summary>
    /// Геодезические преобразования координат Volve:
    /// ED50 / UTM zone 31N → WGS84 (широта/долгота).
    ///
    /// Выгрузки WITSML-траекторий (data/Volve_Well_technical_data/WellWellbore/**) хранят
    /// координаты в проекции ED50 / UTM zone 31N (датум «European 1950 - Mean»,
    /// «Map-Zone: Zone 31N»). Для карты и API нужен WGS84. Преобразование выполняется
    /// двумя шагами: обратная проекция Transverse Mercator (ED50 → геодезические ED50),
    /// затем 7-параметрическое преобразование Helmert «Position Vector» EPSG:1613
    /// «ED50 to WGS 84 (24)» — рекомендованное EPSG для норвежского шельфа южнее 62°N.
    /// </summary>
    public static class CoordinateTransform
    {
        // Эллипсоид International 1924 (ED50).
        private const double Ed50A = 6378388.0;
        private const double Ed50InvF = 297.0;

        // Эллипсоид WGS 84.
        private const double Wgs84A = 6378137.0;
        private const double Wgs84InvF = 298.257223563;

        // Параметры UTM zone 31N.
        private const int UtmZone = 31;
        private const double UtmK0 = 0.9996;
        private const double UtmFalseEasting = 500000.0;
        private const double UtmFalseNorthing = 0.0;

        // EPSG:1613 «ED50 to WGS 84 (24)» — Position Vector transformation (geog2D domain).
        private const double HelmertDx = -90.365;
        private const double HelmertDy = -101.13;
        private const double HelmertDz = -123.384;
        private const double HelmertRxArcSec = 0.333;
        private const double HelmertRyArcSec = 0.077;
        private const double HelmertRzArcSec = 0.894;
        private const double HelmertScalePpm = 1.994;

        private const double ArcSecToRad = Math.PI / 180.0 / 3600.0;
        private const double DegToRad = Math.PI / 180.0;
        private const double RadToDeg = 180.0 / Math.PI;

        /// <summary>
        /// Преобразует координату ED50 / UTM zone 31N (Easting, Northing в метрах)
        /// в WGS84 (широта, долгота в градусах).
        /// </summary>
        public static (double Latitude, double Longitude) Ed50Utm31ToWgs84(double easting, double northing)
        {
            var (ed50Lat, ed50Lon) = UtmToGeodetic(
                easting, northing, UtmZone, Ed50A, Ed50InvF, UtmK0, UtmFalseEasting, UtmFalseNorthing);

            var (x, y, z) = GeodeticToEcef(ed50Lat, ed50Lon, 0.0, Ed50A, Ed50InvF);

            var (xt, yt, zt) = HelmertPositionVector(
                x, y, z,
                HelmertDx, HelmertDy, HelmertDz,
                HelmertRxArcSec * ArcSecToRad,
                HelmertRyArcSec * ArcSecToRad,
                HelmertRzArcSec * ArcSecToRad,
                HelmertScalePpm);

            var (wgsLat, wgsLon) = EcefToGeodetic(xt, yt, zt, Wgs84A, Wgs84InvF);

            return (wgsLat * RadToDeg, wgsLon * RadToDeg);
        }

        /// <summary>
        /// Обратная проекция Transverse Mercator (UTM): Easting/Northing → геодезические
        /// координаты (радианы) на заданном эллипсоиде. Формулы Snyder (1987), p. 63.
        /// </summary>
        private static (double Lat, double Lon) UtmToGeodetic(
            double easting,
            double northing,
            int zone,
            double a,
            double invF,
            double k0,
            double falseEasting,
            double falseNorthing)
        {
            var f = 1.0 / invF;
            var e2 = f * (2.0 - f);
            var ep2 = e2 / (1.0 - e2);

            var lon0 = (zone * 6.0 - 183.0) * DegToRad;

            var m = (northing - falseNorthing) / k0;
            var mu = m / (a * (1.0 - e2 / 4.0 - 3.0 * e2 * e2 / 64.0 - 5.0 * e2 * e2 * e2 / 256.0));

            var e1 = (1.0 - Math.Sqrt(1.0 - e2)) / (1.0 + Math.Sqrt(1.0 - e2));

            var phi1 = mu
                + (3.0 * e1 / 2.0 - 27.0 * e1 * e1 * e1 / 32.0) * Math.Sin(2.0 * mu)
                + (21.0 * e1 * e1 / 16.0 - 55.0 * e1 * e1 * e1 * e1 / 32.0) * Math.Sin(4.0 * mu)
                + (151.0 * e1 * e1 * e1 / 96.0) * Math.Sin(6.0 * mu)
                + (1097.0 * e1 * e1 * e1 * e1 / 512.0) * Math.Sin(8.0 * mu);

            var sinPhi1 = Math.Sin(phi1);
            var cosPhi1 = Math.Cos(phi1);
            var tanPhi1 = Math.Tan(phi1);

            var n1 = a / Math.Sqrt(1.0 - e2 * sinPhi1 * sinPhi1);
            var t1 = tanPhi1 * tanPhi1;
            var c1 = ep2 * cosPhi1 * cosPhi1;
            var r1 = a * (1.0 - e2) / Math.Pow(1.0 - e2 * sinPhi1 * sinPhi1, 1.5);
            var d = (easting - falseEasting) / (n1 * k0);

            var lat = phi1 - (n1 * tanPhi1 / r1) * (
                d * d / 2.0
                - (5.0 + 3.0 * t1 + 10.0 * c1 - 4.0 * c1 * c1 - 9.0 * ep2) * d * d * d * d / 24.0
                + (61.0 + 90.0 * t1 + 298.0 * c1 + 45.0 * t1 * t1 - 252.0 * ep2 - 3.0 * c1 * c1)
                    * d * d * d * d * d * d / 720.0);

            var lon = lon0 + (
                d
                - (1.0 + 2.0 * t1 + c1) * d * d * d / 6.0
                + (5.0 - 2.0 * c1 + 28.0 * t1 - 3.0 * c1 * c1 + 8.0 * ep2 + 24.0 * t1 * t1)
                    * d * d * d * d * d / 120.0) / cosPhi1;

            return (lat, lon);
        }

        /// <summary>Геодезические (радианы) → геоцентрические ECEF (метры).</summary>
        private static (double X, double Y, double Z) GeodeticToEcef(
            double lat,
            double lon,
            double height,
            double a,
            double invF)
        {
            var f = 1.0 / invF;
            var e2 = f * (2.0 - f);

            var sinLat = Math.Sin(lat);
            var cosLat = Math.Cos(lat);
            var n = a / Math.Sqrt(1.0 - e2 * sinLat * sinLat);

            var x = (n + height) * cosLat * Math.Cos(lon);
            var y = (n + height) * cosLat * Math.Sin(lon);
            var z = (n * (1.0 - e2) + height) * sinLat;

            return (x, y, z);
        }

        /// <summary>
        /// 7-параметрическое преобразование Helmert в конвенции «Position Vector»
        /// (EPSG method 9606), как в EPSG:1613.
        /// </summary>
        private static (double X, double Y, double Z) HelmertPositionVector(
            double x,
            double y,
            double z,
            double dx,
            double dy,
            double dz,
            double rx,
            double ry,
            double rz,
            double scalePpm)
        {
            var s = 1.0 + scalePpm * 1e-6;

            var xt = dx + s * (x - rz * y + ry * z);
            var yt = dy + s * (rz * x + y - rx * z);
            var zt = dz + s * (-ry * x + rx * y + z);

            return (xt, yt, zt);
        }

        /// <summary>Геоцентрические ECEF → геодезические WGS84 (радианы). Итеративный метод.</summary>
        private static (double Lat, double Lon) EcefToGeodetic(double x, double y, double z, double a, double invF)
        {
            var f = 1.0 / invF;
            var e2 = f * (2.0 - f);

            var lon = Math.Atan2(y, x);
            var p = Math.Sqrt(x * x + y * y);

            var lat = Math.Atan2(z, p * (1.0 - e2));
            for (var i = 0; i < 6; i++)
            {
                var sinLat = Math.Sin(lat);
                var cosLat = Math.Cos(lat);
                var n = a / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
                var h = p / cosLat - n;
                lat = Math.Atan2(z, p * (1.0 - e2 * n / (n + h)));
            }

            return (lat, lon);
        }
    }
}
