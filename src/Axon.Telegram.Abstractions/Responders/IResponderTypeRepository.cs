namespace Axon.Telegram.Abstractions.Responders;

/// <summary>
/// Represents a type that can serve lists of registered responder types for updates.
/// </summary>
public interface IResponderTypeRepository
{
	/// <summary>
	/// Gets all responder types that are relevant for the given update, and should run before any other responders.
	/// </summary>
	/// <typeparam name="TUpdate">The update type.</typeparam>
	/// <returns>A list of responder types.</returns>
	IReadOnlyList<Type> GetEarlyResponderTypes<TUpdate>();

	/// <summary>
	/// Gets all responder types that are relevant for the given update.
	/// </summary>
	/// <typeparam name="TUpdate">The update type.</typeparam>
	/// <returns>A list of responder types.</returns>
	IReadOnlyList<Type> GetResponderTypes<TUpdate>();

	/// <summary>
	/// Gets all responder types that are relevant for the given update, and should run after any other responders.
	/// </summary>
	/// <typeparam name="TUpdate">The update type.</typeparam>
	/// <returns>A list of responder types.</returns>
	IReadOnlyList<Type> GetLateResponderTypes<TUpdate>();
}
