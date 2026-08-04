namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Raw update handler implementation types registered through
/// <c>AddRawUpdateHandler&lt;THandler&gt;</c>.
/// The dispatcher resolves each type on its own, so a handler that fails to construct cannot
/// stop the remaining handlers from seeing the update. Handlers registered directly against
/// <see cref="IRawUpdateHandler"/> still run, but the container materializes them as one unit.
/// </summary>
public sealed class RawUpdateHandlerRegistry
{
	private readonly List<Type> _handlerTypes = [];

	/// <summary>
	/// Registered handler implementation types, in registration order.
	/// </summary>
	public IReadOnlyList<Type> HandlerTypes => _handlerTypes;

	internal void Add(Type handlerType)
	{
		if (!_handlerTypes.Contains(handlerType))
		{
			_handlerTypes.Add(handlerType);
		}
	}
}
