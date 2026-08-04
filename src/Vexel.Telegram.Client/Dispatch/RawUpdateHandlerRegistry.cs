using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Raw update handler implementation types registered through
/// <c>AddRawUpdateHandler&lt;THandler&gt;</c>.
/// The dispatcher resolves each type on its own, so a handler that fails to construct cannot
/// stop the remaining handlers from seeing the update. Handlers registered directly against
/// <see cref="IRawUpdateHandler"/> still run; the registry also tracks those registrations so the
/// dispatcher can resolve them one at a time instead of as the single unit the container builds.
/// </summary>
public sealed class RawUpdateHandlerRegistry
{
	private readonly List<Type> _handlerTypes = [];
	private readonly HashSet<Type> _handlerTypeSet = [];
	private readonly ConcurrentDictionary<ServiceDescriptor, IRawUpdateHandler> _singletonHandlers = new();
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

	/// <summary>
	/// Backing store for singleton registrations the dispatcher builds itself, so a singleton raw
	/// handler is constructed once for the process rather than once per update.
	/// </summary>
	internal IRawUpdateHandler GetOrCreateSingletonHandler(
		ServiceDescriptor descriptor,
		IServiceProvider provider,
		Func<ServiceDescriptor, IServiceProvider, IRawUpdateHandler> factory) =>
		_singletonHandlers.GetOrAdd(
			descriptor,
			static (key, state) => state.Factory(key, state.Provider),
			(Provider: provider, Factory: factory));

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
