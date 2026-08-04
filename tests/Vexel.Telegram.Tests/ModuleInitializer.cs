using System.Runtime.CompilerServices;

namespace Vexel.Telegram.Tests;

internal static class ModuleInitializer
{
	[ModuleInitializer]
	internal static void Initialize()
	{
		// Empty hook reserved for Verify configuration. Snapshot tests rely on committed .verified files.
	}
}
