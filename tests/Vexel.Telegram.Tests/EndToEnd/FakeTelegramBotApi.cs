using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// Minimal in-process stand-in for the Telegram Bot API server: serves <c>getUpdates</c> over HTTP
/// with real long-poll/offset semantics so the client can be exercised the way a live bot runs.
/// </summary>
public sealed class FakeTelegramBotApi : IAsyncDisposable
{
	private readonly HttpListener _listener = new();
	private readonly CancellationTokenSource _cts = new();
	private readonly List<(int Id, string Json)> _queue = [];
	private readonly List<string> _requests = [];
	private readonly List<string> _outboundCalls = [];
	private readonly Lock _gate = new();
	private readonly Action<string>? _trace;
	private Task? _acceptLoop;
	private int _getUpdatesCalls;
	private int _nextSentMessageId = 9000;

	public FakeTelegramBotApi(Action<string>? trace = null)
	{
		_trace = trace;
		BaseAddress = $"http://127.0.0.1:{FreePort()}";
		_listener.Prefixes.Add($"{BaseAddress}/");
	}

	/// <summary>Base address to hand to <c>TelegramBotClientOptions</c>.</summary>
	public string BaseAddress { get; }

	/// <summary>Number of <c>getUpdates</c> calls served so far.</summary>
	public int GetUpdatesCalls => Volatile.Read(ref _getUpdatesCalls);

	/// <summary>Request log, one line per served API call.</summary>
	public IReadOnlyList<string> Requests
	{
		get
		{
			lock (_gate)
			{
				return [.. _requests];
			}
		}
	}

	/// <summary>
	/// Bot-initiated calls the server received (sendMessage, editMessageText, answerCallbackQuery,
	/// answerInlineQuery, ...), one summary line each, in arrival order.
	/// </summary>
	public IReadOnlyList<string> OutboundCalls
	{
		get
		{
			lock (_gate)
			{
				return [.. _outboundCalls];
			}
		}
	}

	public void Start()
	{
		_listener.Start();
		_acceptLoop = Task.Run(AcceptLoopAsync);
	}

	/// <summary>Queues updates so a single poll delivers them as one batch, like the real API.</summary>
	public void Enqueue(params (int Id, string Json)[] updates)
	{
		lock (_gate)
		{
			_queue.AddRange(updates);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _cts.CancelAsync();
		_listener.Close();

		if (_acceptLoop is not null)
		{
			try
			{
				await _acceptLoop;
			}
			catch (Exception)
			{
				// Listener teardown races are expected.
			}
		}

		_cts.Dispose();
	}

	/// <summary>Builds a text-message update JSON payload.</summary>
	public static (int Id, string Json) MessageUpdate(int id, long chatId, string text)
	{
		var sender = new JsonObject
		{
			["id"] = chatId,
			["is_bot"] = false,
			["first_name"] = $"chat{chatId}",
		};

		var update = new JsonObject
		{
			["update_id"] = id,
			["message"] = new JsonObject
			{
				["message_id"] = id,
				["date"] = 1_700_000_000,
				["chat"] = new JsonObject
				{
					["id"] = chatId,
					["type"] = "private",
					["first_name"] = $"chat{chatId}",
				},
				["from"] = sender,
				["text"] = text,
			},
		};

		return (id, update.ToJsonString());
	}

	/// <summary>
	/// Builds a text-message update the way Telegram delivers a typed command: a <c>bot_command</c>
	/// entity covering the leading <c>/command[@Bot]</c> token.
	/// </summary>
	public static (int Id, string Json) CommandUpdate(int id, long chatId, string text)
	{
		var (_, json) = MessageUpdate(id, chatId, text);
		var update = (JsonObject)JsonNode.Parse(json)!;

		var space = text.IndexOf(' ', StringComparison.Ordinal);
		var length = space < 0 ? text.Length : space;

		update["message"]!["entities"] = new JsonArray(
			new JsonObject
			{
				["type"] = "bot_command",
				["offset"] = 0,
				["length"] = length,
			});

		return (id, update.ToJsonString());
	}

	/// <summary>
	/// Builds a callback-query update JSON payload for a tap on a button under a bot message.
	/// </summary>
	public static (int Id, string Json) CallbackQueryUpdate(
		int id,
		long chatId,
		int botMessageId,
		string callbackQueryId,
		string data)
	{
		var update = new JsonObject
		{
			["update_id"] = id,
			["callback_query"] = new JsonObject
			{
				["id"] = callbackQueryId,
				["from"] = new JsonObject
				{
					["id"] = chatId,
					["is_bot"] = false,
					["first_name"] = $"chat{chatId}",
				},
				["chat_instance"] = $"ci-{chatId}",
				["data"] = data,
				["message"] = new JsonObject
				{
					["message_id"] = botMessageId,
					["date"] = 1_700_000_000,
					["chat"] = new JsonObject
					{
						["id"] = chatId,
						["type"] = "private",
						["first_name"] = $"chat{chatId}",
					},
					["from"] = new JsonObject
					{
						["id"] = 424_242,
						["is_bot"] = true,
						["first_name"] = "VexelBot",
					},
					["text"] = "pick one",
				},
			},
		};

		return (id, update.ToJsonString());
	}

	/// <summary>Builds an inline-query update JSON payload.</summary>
	public static (int Id, string Json) InlineQueryUpdate(
		int id,
		long userId,
		string inlineQueryId,
		string query)
	{
		var update = new JsonObject
		{
			["update_id"] = id,
			["inline_query"] = new JsonObject
			{
				["id"] = inlineQueryId,
				["from"] = new JsonObject
				{
					["id"] = userId,
					["is_bot"] = false,
					["first_name"] = $"user{userId}",
				},
				["query"] = query,
				["offset"] = "",
			},
		};

		return (id, update.ToJsonString());
	}

	/// <summary>
	/// Builds a chosen-inline-result update JSON payload for the result the user picked out of an
	/// inline answer, including the <c>inline_message_id</c> of the message it posted.
	/// </summary>
	public static (int Id, string Json) ChosenInlineResultUpdate(
		int id,
		long userId,
		string resultId,
		string query,
		string inlineMessageId)
	{
		var update = new JsonObject
		{
			["update_id"] = id,
			["chosen_inline_result"] = new JsonObject
			{
				["result_id"] = resultId,
				["from"] = new JsonObject
				{
					["id"] = userId,
					["is_bot"] = false,
					["first_name"] = $"user{userId}",
				},
				["query"] = query,
				["inline_message_id"] = inlineMessageId,
			},
		};

		return (id, update.ToJsonString());
	}

	private static int FreePort()
	{
		using var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		return port;
	}

	private async Task AcceptLoopAsync()
	{
		while (!_cts.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync();
			}
			catch (Exception) when (_cts.IsCancellationRequested || !_listener.IsListening)
			{
				return;
			}

			try
			{
				await ServeAsync(context);
			}
			catch (Exception)
			{
				// A dropped connection during shutdown must not fail the fake server.
			}
		}
	}

	private async Task ServeAsync(HttpListenerContext context)
	{
		var method = context.Request.Url?.Segments[^1].Trim('/') ?? string.Empty;

		using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
		var body = await reader.ReadToEndAsync();

		var payload = method switch
		{
			"getUpdates" => await ServeGetUpdatesAsync(body),
			"getMe" => /*lang=json,strict*/ """{"ok":true,"result":{"id":424242,"is_bot":true,"first_name":"VexelBot","username":"vexel_bot"}}""",
			_ => ServeOutboundCall(method, body),
		};

		var bytes = Encoding.UTF8.GetBytes(payload);
		context.Response.StatusCode = 200;
		context.Response.ContentType = "application/json";
		context.Response.ContentLength64 = bytes.Length;
		await context.Response.OutputStream.WriteAsync(bytes);
		context.Response.Close();
	}

	/// <summary>
	/// Serves everything the bot sends outward. Calls are logged the way a Telegram-side observer
	/// would see them, and replies carry a result shaped like the real API's so the strongly typed
	/// client deserializes it.
	/// </summary>
	private string ServeOutboundCall(string method, string body)
	{
		var request = ParseObject(body);

		lock (_gate)
		{
			_outboundCalls.Add(Describe(method, request));
		}

		_trace?.Invoke($"telegram-api  <- bot calls {Describe(method, request)}");

		return method switch
		{
			"sendMessage" or "editMessageText" when request?["inline_message_id"] is null =>
				$$"""{"ok":true,"result":{{MessageResult(request)}}}""",
			_ => /*lang=json,strict*/ """{"ok":true,"result":true}""",
		};
	}

	private static string Describe(string method, JsonObject? request)
	{
		if (request is null)
		{
			return method;
		}

		var fields = new List<string>();
		foreach (var key in (string[])["chat_id", "message_id", "inline_message_id", "callback_query_id", "inline_query_id", "text", "show_alert", "cache_time", "url", "secret_token", "drop_pending_updates"])
		{
			if (request[key] is { } value)
			{
				// Strings are rendered raw rather than JSON-escaped, so message text in the call log
				// reads the way it does in the chat.
				fields.Add(value.GetValueKind() == JsonValueKind.String
					? $"{key}=\"{value.GetValue<string>()}\""
					: $"{key}={value.ToJsonString()}");
			}
		}

		// Inline answers are only meaningful with their result set, including the empty one the
		// fail-closed obligation sends.
		if (request["results"] is JsonArray results)
		{
			fields.Add(DescribeResults(results));
		}

		// setMyCommands is only meaningful with the menu it publishes.
		if (request["commands"] is JsonArray commands)
		{
			fields.Add(DescribeCommands(commands));
		}

		if (request["reply_markup"] is not null)
		{
			fields.Add("reply_markup=<inline keyboard>");

			// Render the buttons the way the Telegram client would hand them back on a tap, so the
			// callback_data a keyboard helper produced is visible in the call log.
			if (DescribeButtons(request["reply_markup"]?["inline_keyboard"] as JsonArray) is { } buttons)
			{
				fields.Add(buttons);
			}
		}

		return fields.Count == 0 ? method : $"{method} {string.Join(' ', fields)}";
	}

	/// <summary>
	/// Summarises an <c>answerInlineQuery</c> result set as <c>id -> title</c> pairs, the way the
	/// Telegram client would list them under the user's input field.
	/// </summary>
	private static string DescribeResults(JsonArray results)
	{
		var described = results
			.OfType<JsonObject>()
			.Select(static result =>
			{
				var id = result["id"]?.GetValue<string>() ?? string.Empty;
				var title = result["title"]?.GetValue<string>() ?? string.Empty;

				return $"{id} -> \"{title}\"";
			})
			.ToArray();

		return $"results=[{string.Join("; ", described)}]";
	}

	/// <summary>
	/// Summarises a <c>setMyCommands</c> payload as the <c>/command - description</c> menu
	/// BotFather would show the user.
	/// </summary>
	private static string DescribeCommands(JsonArray commands)
	{
		var described = commands
			.OfType<JsonObject>()
			.Select(static command =>
			{
				var name = command["command"]?.GetValue<string>() ?? string.Empty;
				var description = command["description"]?.GetValue<string>() ?? string.Empty;

				return $"/{name} - \"{description}\"";
			})
			.ToArray();

		return $"commands=[{string.Join("; ", described)}]";
	}

	/// <summary>
	/// Summarises an inline keyboard as <c>label -> callback_data</c> (or <c>url</c>) pairs.
	/// </summary>
	private static string? DescribeButtons(JsonArray? rows)
	{
		if (rows is null)
		{
			return null;
		}

		var buttons = rows
			.OfType<JsonArray>()
			.SelectMany(static row => row.OfType<JsonObject>())
			.Select(static button =>
			{
				var label = button["text"]?.GetValue<string>() ?? string.Empty;
				var target = button["callback_data"] is { } data
					? $"callback_data=\"{data.GetValue<string>()}\""
					: button["url"] is { } url
						? $"url=\"{url.GetValue<string>()}\""
						: button["switch_inline_query"] is { } query
							? $"switch_inline_query=\"{query.GetValue<string>()}\""
							: "<other>";

				return $"\"{label}\" -> {target}";
			})
			.ToArray();

		return buttons.Length == 0 ? null : $"buttons=[{string.Join("; ", buttons)}]";
	}

	private string MessageResult(JsonObject? request)
	{
		var chatId = request?["chat_id"]?.GetValue<long>() ?? 0;
		var messageId = request?["message_id"]?.GetValue<int>()
			?? Interlocked.Increment(ref _nextSentMessageId);

		var message = new JsonObject
		{
			["message_id"] = messageId,
			["date"] = 1_700_000_000,
			["chat"] = new JsonObject
			{
				["id"] = chatId,
				["type"] = "private",
			},
			["from"] = new JsonObject
			{
				["id"] = 424_242,
				["is_bot"] = true,
				["first_name"] = "VexelBot",
			},
			["text"] = request?["text"]?.GetValue<string>() ?? string.Empty,
		};

		return message.ToJsonString();
	}

	private static JsonObject? ParseObject(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		try
		{
			return JsonNode.Parse(body) as JsonObject;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private async Task<string> ServeGetUpdatesAsync(string body)
	{
		_ = Interlocked.Increment(ref _getUpdatesCalls);

		var offset = ReadOffset(body);
		List<(int Id, string Json)> batch;

		lock (_gate)
		{
			batch = offset switch
			{
				// Negative offset is the "skip the backlog" probe: the API returns only the newest
				// pending update so the caller can advance past everything older.
				< 0 when _queue.Count > 0 => [_queue[^1]],
				< 0 => [],
				_ => [.. _queue.Where(u => u.Id >= offset)],
			};

			_requests.Add(string.Create(
				CultureInfo.InvariantCulture,
				$"getUpdates offset={offset} -> [{string.Join(",", batch.Select(u => u.Id))}]"));
		}

		_trace?.Invoke(string.Create(
			CultureInfo.InvariantCulture,
			$"telegram-api  getUpdates(offset={offset}) -> {(batch.Count == 0 ? "no updates" : string.Join(", ", batch.Select(u => $"update {u.Id}")))}"));

		if (batch.Count == 0)
		{
			// Stand in for a real long poll so the receive loop is not spun at full speed.
			await Task.Delay(100);
		}

		return $$"""{"ok":true,"result":[{{string.Join(",", batch.Select(u => u.Json))}}]}""";
	}

	private static int ReadOffset(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return 0;
		}

		using var document = JsonDocument.Parse(body);

		return document.RootElement.TryGetProperty("offset", out var offset)
			? offset.GetInt32()
			: 0;
	}
}
