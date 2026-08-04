using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Tests.Fakes;

/// <summary>
/// Records <see cref="ITelegramBotClient.SendRequest{TResponse}"/> calls for unit tests.
/// </summary>
public sealed class RecordingTelegramBotClient : ITelegramBotClient
{
	private int _messageId;

	/// <summary>Every request sent through this client, in order.</summary>
	public ConcurrentQueue<object> Requests { get; } = [];

	/// <inheritdoc />
	public bool LocalBotServer => false;

	/// <inheritdoc />
	public long BotId => 424242;

	/// <inheritdoc />
	public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

	/// <inheritdoc />
	public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

	/// <inheritdoc />
	public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest
	{
		add { }
		remove { }
	}

	/// <inheritdoc />
	public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived
	{
		add { }
		remove { }
	}

	/// <inheritdoc />
	public Task<TResponse> SendRequest<TResponse>(
		IRequest<TResponse> request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		Requests.Enqueue(request);

		if (typeof(TResponse) == typeof(Message))
		{
			var id = Interlocked.Increment(ref _messageId);
			var message = new Message
			{
				Id = id,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 0 },
				Text = "ok",
			};
			return Task.FromResult((TResponse)(object)message);
		}

		if (typeof(TResponse) == typeof(bool))
		{
			return Task.FromResult((TResponse)(object)true);
		}

		return Task.FromResult(default(TResponse)!);
	}

	/// <inheritdoc />
	public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

	/// <inheritdoc />
	public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	/// <inheritdoc />
	public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	/// <summary>Requests of type <typeparamref name="TRequest"/> recorded so far.</summary>
	public IReadOnlyList<TRequest> OfType<TRequest>() =>
		[.. Requests.OfType<TRequest>()];
}
