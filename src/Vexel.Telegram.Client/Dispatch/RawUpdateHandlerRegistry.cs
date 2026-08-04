using Microsoft.Extensions.DependencyInjection;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Raw update handler implementation types registered through
/// <c>AddRawUpdateHandler&lt;THandler&gt;</c>.
/// The dispatcher resolves each type on its own, so a handler that fails to construct cannot
/// stop the remaining handlers from seeing the update. Handlers registered directly against
/// <see cref="IRawUpdateHandler"/> still run through the container as a set; the registry also
/// tracks those registrations so the dispatcher can degrade to resolving them one at a time when
/// that set cannot be built.
/// </summary>
public sealed class RawUpdateHandlerRegistry
{
	private readonly List<Type> _handlerTypes = [];
	private readonly HashSet<Type> _handlerTypeSet = [];
	private IServiceCollection? _services;
	private ServiceDescriptor[]? _containerRegistrations;

	/// <summary>
	/// Registered handler implementation types, in registration order.
	/// </summary>
	public IReadOnlyList<Type> HandlerTypes => _handlerTypes;

	/// <summary>
	/// Non-keyed service registrations made directly against <see cref="IRawUpdateHandler"/>, in
	/// registration order. Internal so the snapshot is only ever taken from the dispatcher, i.e.
	/// after the container is built and the registration list is final.
	/// Empty when the registry was created without a service collection.
	/// </summary>
	internal IReadOnlyList<ServiceDescriptor> ContainerRegistrations =>
		_containerRegistrations ??= SnapshotContainerRegistrations();

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

	internal void AttachServices(IServiceCollection services) => _services = services;

	private ServiceDescriptor[] SnapshotContainerRegistrations()
	{
		if (_services is not { } services)
		{
			return [];
		}

		var containerRegistrations = services.Where(static descriptor =>
			!descriptor.IsKeyedService && descriptor.ServiceType == typeof(IRawUpdateHandler));

		return [.. containerRegistrations];
	}
}
