namespace Vexel.Telegram.Extensions.Builders;

/// <inheritdoc />
public abstract class BuilderBase<TEntity> : IBuilder<TEntity>
{
	/// <inheritdoc />
	public abstract TEntity Build();
}
