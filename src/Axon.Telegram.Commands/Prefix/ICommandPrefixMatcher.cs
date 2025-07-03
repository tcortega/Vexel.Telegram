using Remora.Results;

namespace Axon.Telegram.Commands.Prefix;

/// <summary>
/// Provides matching logic for text-based command prefixes.
/// </summary>
public interface ICommandPrefixMatcher
{
	/// <summary>
	/// Determines whether the message contents begin or match some accepted command prefix.
	/// </summary>
	/// <param name="content">The message contents to check.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>
	/// A tuple which indicates whether the contents match an accepted prefix, and if so, the index at which the actual
	/// command contents start.
	/// </returns>
	ValueTask<Result<(bool Matches, int ContentStartIndex)>> MatchesPrefixAsync
	(
		string content,
		CancellationToken ct = default
	);
}
