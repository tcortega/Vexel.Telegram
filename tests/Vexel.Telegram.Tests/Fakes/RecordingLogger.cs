using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Vexel.Telegram.Tests.Fakes;

/// <summary>
/// Captures log entries so tests can assert on level and message.
/// </summary>
/// <typeparam name="T">Logger category.</typeparam>
public sealed class RecordingLogger<T> : ILogger<T>
{
	/// <summary>Every entry logged through this logger, in order.</summary>
	public ConcurrentQueue<LogEntry> Entries { get; } = [];

	/// <inheritdoc />
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel) => true;

	/// <inheritdoc />
	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		ArgumentNullException.ThrowIfNull(formatter);
		Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
	}

	/// <summary>A single captured log entry.</summary>
	/// <param name="Level">Log level.</param>
	/// <param name="Message">Formatted message.</param>
	/// <param name="Exception">Optional exception.</param>
	public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
