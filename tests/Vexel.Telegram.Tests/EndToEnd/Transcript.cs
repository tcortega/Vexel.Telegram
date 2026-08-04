using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// Timestamped record of everything the running bot did, so a reader can see ordering,
/// parallelism and fault isolation directly instead of inferring them from assertions.
/// </summary>
public sealed class Transcript
{
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private readonly ConcurrentQueue<string> _lines = [];

	public IReadOnlyList<string> Lines => [.. _lines];

	public void Write(string line) =>
		_lines.Enqueue(string.Create(
			CultureInfo.InvariantCulture,
			$"[{_clock.Elapsed.TotalSeconds,6:F3}s] {line}"));

	/// <summary>Position of the first line containing <paramref name="fragment"/>, or -1.</summary>
	public int IndexOf(string fragment)
	{
		var lines = Lines;
		for (var i = 0; i < lines.Count; i++)
		{
			if (lines[i].Contains(fragment, StringComparison.Ordinal))
			{
				return i;
			}
		}

		return -1;
	}

	public bool Contains(string fragment) => IndexOf(fragment) >= 0;
}

/// <summary>Routes the library's own log output into the transcript.</summary>
public sealed class TranscriptLoggerProvider(Transcript transcript) : ILoggerProvider
{
	public ILogger CreateLogger(string categoryName) =>
		new TranscriptLogger(transcript, categoryName.Split('.')[^1]);

	public void Dispose()
	{
	}

	private sealed class TranscriptLogger(Transcript transcript, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel))
			{
				return;
			}

			var suffix = exception is null ? string.Empty : $" ({exception.GetType().Name}: {exception.Message})";
			transcript.Write($"{logLevel,-11} {category,-16} {formatter(state, exception)}{suffix}");
		}
	}
}
