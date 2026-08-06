namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Raw update handler implementation types registered through
/// <c>AddRawUpdateHandler&lt;THandler&gt;</c>.
/// The dispatcher resolves each type on its own, so a handler that fails to construct cannot
/// stop the remaining handlers from seeing the update.
/// </summary>
internal sealed class RawUpdateHandlerRegistry
{
	private readonly List<Type> _handlerTypes = [];
	private readonly HashSet<Type> _handlerTypeSet = [];

	/// <summary>
	/// Registered handler implementation types, in registration order.
	/// </summary>
	public IReadOnlyList<Type> HandlerTypes => _handlerTypes;

	internal void Add(Type handlerType)
	{
		if (_handlerTypeSet.Add(handlerType))
		{
			_handlerTypes.Add(handlerType);
		}
	}
}
