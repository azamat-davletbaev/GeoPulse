using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace GeoPulse.Messaging
{
    /// <summary>
    /// Создаёт топик при старте консьюмера, если его ещё нет.
    ///
    /// Зачем: подписка на несуществующий топик возвращает UNKNOWN_TOPIC_OR_PARTITION, а Kafka
    /// начиная с KIP-361 не создаёт топик по запросу метаданных консьюмера (даже при
    /// broker-side auto.create.topics.enable=true и allow.auto.create.topics=true) — топик
    /// появляется только от продюсера или явного CreateTopics. Без этого сервис падал при
    /// старте до первого запуска ingestion.
    /// </summary>
    public sealed class TopicProvisioner
    {
        private readonly ILogger<TopicProvisioner> _logger;

        /// <summary>Конструктор.</summary>
        public TopicProvisioner(ILogger<TopicProvisioner> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Идемпотентно создаёт топик. Ошибки создания не пробрасываются: консьюмер продолжит
        /// работу и повторит чтение (топик может создать другой сервис или первый запуск ingestion).
        /// </summary>
        /// <param name="bootstrapServers">Адрес брокера.</param>
        /// <param name="topic">Имя топика.</param>
        /// <param name="partitions">Число партиций (если топик создаём мы).</param>
        /// <param name="replicationFactor">Фактор репликации (локально и в прод-стенде — 1).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task EnsureAsync(
            string bootstrapServers,
            string topic,
            int partitions,
            short replicationFactor,
            CancellationToken cancellationToken = default)
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

            try
            {
                await admin.CreateTopicsAsync(
                    new[]
                    {
                        new TopicSpecification
                        {
                            Name = topic,
                            NumPartitions = partitions,
                            ReplicationFactor = replicationFactor
                        }
                    });

                _logger.LogInformation("Топик {Topic} создан: партиций {Partitions}", topic, partitions);
            }
            catch (CreateTopicsException ex)
                when (ex.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                _logger.LogInformation("Топик {Topic} уже существует", topic);
            }
            catch (KafkaException ex)
            {
                _logger.LogWarning(ex, "Не удалось создать топик {Topic}; чтение начнётся, когда он появится", topic);
            }
        }
    }
}
