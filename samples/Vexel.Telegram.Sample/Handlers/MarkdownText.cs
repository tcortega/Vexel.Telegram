using System.Text;

namespace Vexel.Telegram.Sample.Handlers;

/// <summary>
/// Escapes user-supplied text for legacy <c>ParseMode.Markdown</c>. Telegram rejects a message
/// whose entities are unbalanced, so anything a user controls has to be escaped before it is
/// interpolated into a Markdown literal.
/// </summary>
internal static class MarkdownText
{
	internal static string Escape(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}

		var builder = new StringBuilder(value.Length);
		foreach (var character in value)
		{
			// The four delimiters legacy Markdown honours; a '\' before anything else stays literal.
			if (character is '_' or '*' or '`' or '[')
			{
				_ = builder.Append('\\');
			}

			_ = builder.Append(character);
		}

		return builder.ToString();
	}
}
