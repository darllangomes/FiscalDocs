namespace FiscalDocs.Infrastructure.Messaging;

/// <summary>Quanto tempo as mensagens já publicadas ficam na outbox, de quanto em quanto tempo a limpeza roda e o tamanho do lote.</summary>
public sealed record OutboxCleanupOptions
{
    public const int DefaultRetentionDays = 7;
    public const int DefaultIntervalMinutes = 60;
    public const int DefaultBatchSize = 1000;

    public OutboxCleanupOptions(TimeSpan Retention, TimeSpan Interval, int BatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Retention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(BatchSize, 0);

        this.Retention = Retention;
        this.Interval = Interval;
        this.BatchSize = BatchSize;
    }

    public TimeSpan Retention { get; }
    public TimeSpan Interval { get; }
    public int BatchSize { get; }
}
