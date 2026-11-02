using System.Text.Json;

namespace GeoPulse.Core.Events
{
    /// <summary>Общие настройки сериализации событий GeoPulse.</summary>
    public static class MessagingJson
    {
        /// <summary>
        /// Опции System.Text.Json для событий: camelCase, регистронезависимое чтение.
        /// Даты сериализуются встроенным конвертером DateOnly в ISO-8601 (yyyy-MM-dd).
        /// </summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }
}
