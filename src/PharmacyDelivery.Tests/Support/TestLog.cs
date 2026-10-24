using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PharmacyDelivery.Tests;

/// <summary>One captured log record.</summary>
public sealed record TestLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Logs written by the code under test: kept for assertions (<c>Logs.Entries</c>) and echoed to the NUnit output,
/// so a failing test shows what the service logged.
/// </summary>
public sealed class TestLog : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> _entries = new();

    public IReadOnlyList<TestLogEntry> Entries => [.. _entries];

    public IEnumerable<TestLogEntry> AtLeast(LogLevel level) => Entries.Where(e => e.Level >= level);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, TestLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            log._entries.Enqueue(new TestLogEntry(category, logLevel, message, exception));
            TestContext.Out.WriteLine($"[{logLevel}] {category}: {message}");
        }
    }
}
