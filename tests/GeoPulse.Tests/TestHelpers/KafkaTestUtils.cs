using System.Text;

namespace GeoPulse.Tests.TestHelpers;

internal static class KafkaTestUtils
{
    public static async Task CreateTopicAsync(string bootstrapServers, string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }
        });
    }

    public static async Task ProduceAsync(string bootstrapServers, string topic, string key, string value)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();
        await producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = value });
    }

    public static IProducer<string, string> BuildProducer(string bootstrapServers) =>
        new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();

    /// <summary>Блокирующее чтение одного сообщения из топика (для проверки DLQ).</summary>
    public static ConsumeResult<string, string>? ConsumeOne(
        string bootstrapServers,
        string topic,
        int timeoutMilliseconds = 30_000)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"test-dlq-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    public static string? GetHeader(Message<string, string> message, string key)
    {
        var header = message.Headers.FirstOrDefault(h => h.Key == key);
        return header is null ? null : Encoding.UTF8.GetString(header.GetValueBytes());
    }
}
