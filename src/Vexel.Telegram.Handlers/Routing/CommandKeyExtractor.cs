using System.Diagnostics.CodeAnalysis;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// Extracts a command key and argument payload from a message per the V2 binding convention.
/// </summary>
public static class CommandKeyExtractor
{
	/// <summary>
	/// Tries to read a bot command from <paramref name="message"/>.
	/// </summary>
	/// <param name="message">Inbound message.</param>
	/// <param name="botUsername">
	/// This bot's username without <c>@</c>, or <see langword="null"/> when unknown.
	/// When the command carries an <c>@OtherBot</c> suffix that does not match, the message is
	/// not treated as a command for this bot.
	/// </param>
	/// <param name="command">Command name without leading slash, when this method returns true.</param>
	/// <param name="arguments">
	/// Text after the command entity, trimmed. Empty when the user sent only the command.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a command entity at offset 0 targets this bot.
	/// </returns>
	public static bool TryExtract(
		Message message,
		string? botUsername,
		[NotNullWhen(true)] out string? command,
		[NotNullWhen(true)] out string? arguments)
	{
		ArgumentNullException.ThrowIfNull(message);

		command = null;
		arguments = null;

		// Captions are not command-routed in 2.0.
		if (message.Text is not { Length: > 0 } text
			|| message.Entities is not { Length: > 0 } entities)
		{
			return false;
		}

		MessageEntity? entity = null;
		foreach (var candidate in entities)
		{
			if (candidate.Type == MessageEntityType.BotCommand && candidate.Offset == 0)
			{
				entity = candidate;
				break;
			}
		}

		if (entity is null || entity.Length <= 0 || entity.Length > text.Length)
		{
			return false;
		}

		var raw = text.Substring(entity.Offset, entity.Length);
		if (raw.Length == 0)
		{
			return false;
		}

		// Telegram includes the leading '/'.
		var body = raw[0] == '/' ? raw[1..] : raw;
		if (body.Length == 0)
		{
			return false;
		}

		var at = body.IndexOf('@', StringComparison.Ordinal);
		if (at >= 0)
		{
			var suffix = body[(at + 1)..];
			if (botUsername is not null
				&& !suffix.Equals(botUsername, StringComparison.OrdinalIgnoreCase))
			{
				// Directed at another bot - fall through to flow/On*.
				return false;
			}

			body = body[..at];
			if (body.Length == 0)
			{
				return false;
			}
		}

		var argsStart = entity.Offset + entity.Length;
		var args = argsStart < text.Length
			? text[argsStart..].Trim()
			: string.Empty;

		command = body;
		arguments = args;
		return true;
	}
}
