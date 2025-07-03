using Remora.Results;

namespace Axon.Telegram.Extensions.Builders;

/// <summary>
/// Represents an object responsible for constructing and validating a model.
/// </summary>
/// <typeparam name="TEntity">The type of model to build.</typeparam>
public interface IBuilder<out TEntity>
{
	/// <summary>
	/// Validates and then builds the model.
	/// </summary>
	/// <returns>A <see cref="Result{TEntity}"/> containing the result of the build or the reason for failure.</returns>
	TEntity Build();
}
