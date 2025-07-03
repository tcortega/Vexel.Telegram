using Vexel.Telegram.Commands.Responders;
using Microsoft.Extensions.Options;
using Remora.Results;

namespace Vexel.Telegram.Commands.Prefix;

/// <summary>
/// Provides simple static prefix matching.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SimplePrefixMatcher"/> class.
/// </remarks>
/// <param name="options">The responder options.</param>
public class SimplePrefixMatcher(IOptions<CommandResponderOptions> options) : ICommandPrefixMatcher
{
	private readonly CommandResponderOptions _options = options.Value;

	/// <inheritdoc />
	public ValueTask<Result<(bool Matches, int ContentStartIndex)>> MatchesPrefixAsync
	(
		string content,
		CancellationToken ct = default
	)
	{
		if (_options.Prefix is null)
		{
			return new((true, 0));
		}

		if (!content.StartsWith(_options.Prefix, StringComparison.Ordinal))
		{
			return new((false, -1));
		}

		var index = content.IndexOf(_options.Prefix, StringComparison.Ordinal) + _options.Prefix.Length;
		return new((true, index));
	}
}
