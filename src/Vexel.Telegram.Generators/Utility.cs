using Microsoft.CodeAnalysis;

namespace Vexel.Telegram.Generators;

internal static class ArgumentGuard
{
	public static void ThrowIfNull(object? argument)
	{
		if (argument is null)
		{
			throw new ArgumentNullException(nameof(argument));
		}
	}
}

internal static class Utility
{
	public static string? NullIf(this string value, string check) =>
		value.Equals(check, StringComparison.Ordinal) ? null : value;

	public static IncrementalValuesProvider<T> WhereNotNull<T>(this IncrementalValuesProvider<T?> values)
		where T : class =>
		values.Where(static x => x is not null)!;

	public static string SanitizeAssemblyName(string assemblyName) =>
		assemblyName
			.Replace(".", string.Empty)
			.Replace(" ", string.Empty)
			.Replace("-", string.Empty)
			.Trim();

	public static string EscapeString(string value) =>
		value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\r", "\\r")
			.Replace("\n", "\\n");
}
