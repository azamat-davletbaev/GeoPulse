using System.Globalization;
using GeoPulse.Core.Events;

namespace GeoPulse.Ingestion.Service
{
    /// <summary>Результат разбора текстового файла траектории WITSML (*_ACTUAL).</summary>
    public sealed record ParsedTrajectory(double? TdMd, double? TdTvd, double? SurfaceEasting, double? SurfaceNorthing, IReadOnlyList<TrajectoryPointData> Points);

    /// <summary>
    /// Парсер текстовых выгрузок траекторий Volve
    /// (data/Volve_Well_technical_data/WellWellbore/&lt;скважина&gt;/&lt;ствол&gt;/*_ACTUAL).
    ///
    /// Формат файла (не XML): заголовок с парами «Bottom Hole MD: …m», «Bottom Hole TVD: …m»,
    /// затем секция «SURVEY LIST» и таблица замеров с колонками
    /// MD, Inc, Azim, TVD, X-Offset, Y-Offset, UTM E/W, UTM N/S, DLS.
    /// Координаты — ED50 / UTM zone 31N (Easting = UTM E/W, Northing = UTM N/S).
    /// </summary>
    public static class TrajectoryParser
    {
        /// <summary>Парсит содержимое файла траектории; null — если секция SURVEY LIST не найдена.</summary>
        public static ParsedTrajectory? Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var lines = content
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var tdMd = ParseHeaderValue(lines, "Bottom Hole MD:");
            var tdTvd = ParseHeaderValue(lines, "Bottom Hole TVD:");
            var surfaceEasting = ParseHeaderValue(lines, "Surface EW:");
            var surfaceNorthing = ParseHeaderValue(lines, "Surface NS:");

            var surveyIndex = Array.FindIndex(lines, line =>
                line.Trim().Equals("SURVEY LIST", StringComparison.OrdinalIgnoreCase));
            if (surveyIndex < 0)
            {
                return null;
            }

            var points = new List<TrajectoryPointData>();

            // После «SURVEY LIST» идут две строки заголовка таблицы (имена и единицы).
            for (var i = surveyIndex + 3; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 8)
                {
                    // Строка вне таблицы (например, секция комментариев) — пропускаем.
                    continue;
                }

                if (!TryParseDouble(parts[0], out var md))
                {
                    continue;
                }

                TryParseDouble(parts[1], out var inclination);
                TryParseDouble(parts[2], out var azimuth);
                TryParseDouble(parts[3], out var tvd);
                TryParseDouble(parts[6], out var easting);
                TryParseDouble(parts[7], out var northing);

                points.Add(new TrajectoryPointData
                {
                    Md = md,
                    Tvd = tvd,
                    Inclination = inclination,
                    Azimuth = azimuth,
                    Easting = easting,
                    Northing = northing
                });
            }

            return new ParsedTrajectory(tdMd, tdTvd, surfaceEasting, surfaceNorthing, points);
        }

        private static double? ParseHeaderValue(IReadOnlyList<string> lines, string label)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colon = trimmed.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                var raw = trimmed[(colon + 1)..].Trim().TrimEnd('m', 'M').Trim();
                return TryParseDouble(raw, out var value) ? value : null;
            }

            return null;
        }

        private static bool TryParseDouble(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
