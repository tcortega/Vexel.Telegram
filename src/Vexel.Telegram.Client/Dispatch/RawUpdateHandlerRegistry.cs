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
	private readonly HashSet<Type> _handlerTypeSet = [];

	/// <summary>
	/// Registered handler implementation types, in registration order.
	/// </summary>
	public IReadOnlyList<Type> HandlerTypes => _handlerTypes;

	/// <summary>
	/// Whether <paramref name="handlerType"/> is dispatched through this registry. Handlers that
	/// are also registered against <see cref="IRawUpdateHandler"/> run once, not once per
	/// registration style.
	/// </summary>
	/// <param name="handlerType">The handler implementation type.</param>
	/// <returns><see langword="true"/> when the type is registered here.</returns>
	public bool Contains(Type handlerType) => _handlerTypeSet.Contains(handlerType);

	internal void Add(Type handlerType)
	{
		if (_handlerTypeSet.Add(handlerType))
		{
			_handlerTypes.Add(handlerType);
		}
	}
}
