using System.Text.Json;
using Microsoft.Extensions.Options;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;

namespace Vexel.Telegram.Handlers;

/// <summary>
/// Per-update conversation helper. Arms the next typed text step, reads/writes a JSON draft,
/// and cancels the active flow for the contextual (chat, user) pair.
/// </summary>
public sealed class Flow
{
	private static readonly JsonSerializerOptions s_serializerOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
	};

	private readonly IFlowStore _store;
	private readonly UpdateContextHolder _holder;
	private readonly TelegramRouter _router;
	private readonly TimeProvider _timeProvider;
	private readonly FlowOptions _options;

	/// <summary>
	/// Initializes a new scoped flow helper.
	/// </summary>
	/// <param name="store">Flow state store.</param>
	/// <param name="holder">Per-update context holder.</param>
	/// <param name="router">Router that owns the composed flow step map.</param>
	/// <param name="timeProvider">Clock used for TTL.</param>
	/// <param name="options">Flow options.</param>
	public Flow(
		IFlowStore store,
		UpdateContextHolder holder,
		TelegramRouter router,
		TimeProvider timeProvider,
		IOptions<FlowOptions> options)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(holder);
		ArgumentNullException.ThrowIfNull(router);
		ArgumentNullException.ThrowIfNull(timeProvider);
		ArgumentNullException.ThrowIfNull(options);

		_store = store;
		_holder = holder;
		_router = router;
		_timeProvider = timeProvider;
		_options = options.Value;
	}

	/// <summary>
	/// True when <see cref="PromptAsync{TRequest}"/> armed (or re-armed) a step during this update.
	/// Used by the router to decide auto-complete on successful return.
	/// </summary>
	public bool WasRearmed { get; private set; }

	/// <summary>
	/// True when <see cref="CancelAsync"/> cleared state during this update.
	/// </summary>
	public bool WasCleared { get; private set; }

	/// <summary>
	/// Arms the next text step by its <em>request</em> type, not by the handler class:
	/// <c>flow.PromptAsync&lt;CollectName.Command&gt;()</c>. Immediate.Handlers handler types are
	/// <see langword="static"/> and C# rejects static types as type arguments, so the request record
	/// nested in the handler is what identifies the step.
	/// The step key is <c>typeof(TRequest).FullName</c>; <typeparamref name="TRequest"/> must be the
	/// request of a <c>[Handler]</c> and be flow-bindable (empty record or single <see cref="string"/>)
	/// so the generator registered it in the flow step map (VEX0007 enforces this at compile time).
	/// </summary>
	/// <typeparam name="TRequest">Request type of the handler that receives the next text message.</typeparam>
	/// <param name="ttl">Optional override of <see cref="FlowOptions.DefaultTtl"/>.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async ValueTask PromptAsync<TRequest>(
		TimeSpan? ttl = null,
		CancellationToken cancellationToken = default)
	{
		var stepKey = GetStepKey(typeof(TRequest));
		if (!_router.HasFlowStep(stepKey))
		{
			throw new InvalidOperationException(
				$"Type '{typeof(TRequest).FullName}' is not a registered flow step. "
				+ "Pass the request type of a [Handler] (for example CollectName.Command) whose request "
				+ "is flow-bindable (empty record or single string).");
		}

		var (chatId, userId) = RequireChatUser();
		var existing = await _store.GetAsync(chatId, userId, cancellationToken).ConfigureAwait(false);
		var lifetime = ttl ?? _options.DefaultTtl;
		if (lifetime <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(ttl), lifetime, "Flow TTL must be positive.");
		}

		var entry = new FlowEntry(
			StepKey: stepKey,
			DraftJson: existing?.DraftJson,
			ExpiresAt: _timeProvider.GetUtcNow() + lifetime);

		await _store.SetAsync(chatId, userId, entry, cancellationToken).ConfigureAwait(false);
		WasRearmed = true;
		WasCleared = false;
	}

	/// <summary>
	/// Clears the armed step and draft for the contextual (chat, user) pair.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
	{
		var (chatId, userId) = RequireChatUser();
		await _store.CompleteAsync(chatId, userId, cancellationToken).ConfigureAwait(false);
		WasCleared = true;
		WasRearmed = false;
	}

	/// <summary>
	/// Deserializes the draft POCO for the active flow, or returns <see langword="default"/> when
	/// no entry or no draft is stored.
	/// </summary>
	/// <typeparam name="T">JSON-serializable draft type.</typeparam>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async ValueTask<T?> GetDraftAsync<T>(CancellationToken cancellationToken = default)
	{
		var (chatId, userId) = RequireChatUser();
		var entry = await _store.GetAsync(chatId, userId, cancellationToken).ConfigureAwait(false);
		if (entry?.DraftJson is not { Length: > 0 } json)
		{
			return default;
		}

		return JsonSerializer.Deserialize<T>(json, s_serializerOptions);
	}

	/// <summary>
	/// Serializes and stores <paramref name="draft"/> on the active flow entry.
	/// Requires an armed flow (call <see cref="PromptAsync{TRequest}"/> first, or re-arm after).
	/// </summary>
	/// <typeparam name="T">JSON-serializable draft type.</typeparam>
	/// <param name="draft">Draft payload.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async ValueTask SetDraftAsync<T>(T draft, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(draft);

		var (chatId, userId) = RequireChatUser();
		var entry = await _store.GetAsync(chatId, userId, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException(
				"Cannot set a flow draft without an armed step. Call PromptAsync<TRequest> first.");

		var json = JsonSerializer.Serialize(draft, s_serializerOptions);
		var updated = entry with { DraftJson = json };
		await _store.SetAsync(chatId, userId, updated, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Step key used by the generator and store for <paramref name="requestType"/>.</summary>
	internal static string GetStepKey(Type requestType)
	{
		ArgumentNullException.ThrowIfNull(requestType);
		return requestType.FullName
			?? throw new InvalidOperationException(
				$"Type '{requestType.Name}' has no FullName and cannot be a flow step key.");
	}

	private (long ChatId, long UserId) RequireChatUser()
	{
		if (_holder.Message is { } message)
		{
			var userId = message.UserId
				?? throw new InvalidOperationException(
					"Flow requires Message.From (user id) on the current update.");
			return (message.ChatId, userId);
		}

		if (_holder.Callback is { } callback)
		{
			var chatId = callback.ChatId
				?? throw new InvalidOperationException(
					"Flow requires a chat id; inline-origin callbacks without a message cannot arm text steps.");
			return (chatId, callback.From.Id);
		}

		throw new InvalidOperationException(
			"Flow requires a message or callback update context with a chat and user id.");
	}
}
