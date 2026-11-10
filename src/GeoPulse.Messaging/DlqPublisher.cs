using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace GeoPulse.Messaging
{
    /// <summary>
    /// Публикация необработанных сообщений в DLQ-топик: оригинальные ключ и тело
    /// без переупаковки плюс заголовки <c>reason</c> и <c>source-topic</c>.
    /// Общий для всех консьюмеров, чтобы формат DLQ не разъезжался между сервисами.
    /// </summary>
    public sealed class DlqPublisher
    {
        private readonly IProducer<string, string> _producer;
        private readonly ILogger<DlqPublisher> _logger;

        /// <summary>Конструктор.</summary>
        public DlqPublisher(IProducer<string, string> producer, ILogger<DlqPublisher> logger)
        {
            _producer = producer;
            _logger = logger;
        }

        /// <summary>Дожидается отправки накопленных сообщений (вызывается при остановке сервиса).</summary>
        /// <param name="timeout">Максимальное время ожидания.</param>
        public void Flush(TimeSpan timeout) => _producer.Flush(timeout);

        /// <summary>Отправляет сообщение в DLQ с указанной причиной; ошибки публикации только логируются.</summary>
        /// <param name="dlqTopic">Топик DLQ.</param>
        /// <param name="result">Исходное сообщение из основного топика.</param>
        /// <param name="reason">Причина отбраковки из <see cref="Core.Events.DlqReasons"/>.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task PublishAsync(
            string dlqTopic,
            ConsumeResult<string, string> result,
            string reason,
            CancellationToken cancellationToken = default)
        {
            var message = new Message<string, string>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = new Headers
                {
                    { "reason", Encoding.UTF8.GetBytes(reason) },
                    { "source-topic", Encoding.UTF8.GetBytes(result.Topic) }
                }
            };

            try
            {
                await _producer.ProduceAsync(dlqTopic, message, cancellationToken);

                var rawBody = result.Message.Value ?? string.Empty;
                var bodyPreview = rawBody.Length <= 100 ? rawBody : rawBody[..100];
                _logger.LogInformation(
                    "DLQ: eventType={EventType}, reason={Reason}, key={Key}, body={Body}",
                    EventClassifier.EventTypeOf(rawBody),
                    reason,
                    result.Message.Key,
                    bodyPreview);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Ошибка отправки в DLQ-топик {Topic}", dlqTopic);
            }
        }
    }
}
