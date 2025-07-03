using Axon.Telegram.Abstractions.Responders;

namespace Axon.Telegram.Client.Extensions;

/// <summary>
/// Defines extension methods for the <see cref="Type"/> class.
/// </summary>
public static class TypeExtensions
{
	/// <summary>
	/// Checks if the <see cref="Type"/> implements <see cref="IResponder{TUpdate}"/>.
	/// </summary>
	/// <param name="type">The <see cref="Type"/> to check against.</param>
	/// <returns>True if the type implements <see cref="IResponder{T}"/>.</returns>
	public static bool IsResponder(this Type type)
	{
		var interfaces = type.GetInterfaces();
		return interfaces.Any
		(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResponder<>)
		);
	}
}
