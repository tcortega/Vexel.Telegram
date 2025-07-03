namespace Vexel.Telegram.Commands.Contexts;

/// <summary>
/// Assists with injection of an <see cref="IOperationContext"/> into a service provider.
/// </summary>
public class ContextInjectionService
{
	/// <summary>
	/// Gets or sets the context.
	/// </summary>
	public IOperationContext? Context { get; set; }
}
