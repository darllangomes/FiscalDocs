using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FiscalDocs.IntegrationTests.Support;

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = $"{category} | {formatter(state, exception)}";
            if (exception is not null)
                text += Environment.NewLine + exception; // mensagem + stack trace

            sink.Enqueue(text);
        }
    }
}
