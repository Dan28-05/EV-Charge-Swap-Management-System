namespace EVChargeSwap.Common.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "ev.events";
    public int PublishBatchSize { get; set; } = 50;
    public int PollIntervalSeconds { get; set; } = 1;
    public int MaxRetryCount { get; set; } = 5;
    public bool Enabled { get; set; } = true;
}
