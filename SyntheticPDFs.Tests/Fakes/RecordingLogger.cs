using Microsoft.Extensions.Logging;

namespace SyntheticPDFs.Tests.Fakes
{
    // captures formatted log messages so tests can assert on what was surfaced
    public class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, String Message)> Entries { get; } = new();

        public IEnumerable<String> Warnings =>
            Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, String> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
