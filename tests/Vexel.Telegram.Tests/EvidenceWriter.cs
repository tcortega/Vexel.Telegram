namespace Vexel.Telegram.Tests;

/// <summary>
/// Writes a human-readable record of what a test exercised to the directory named by
/// <c>VEXEL_EVIDENCE_DIR</c>. No-op when the variable is unset, so ordinary test runs write nothing.
/// </summary>
internal static class EvidenceWriter
{
	public static string? TryWrite(string fileName, string content)
	{
		var directory = Environment.GetEnvironmentVariable("VEXEL_EVIDENCE_DIR");
		if (string.IsNullOrWhiteSpace(directory))
		{
			return null;
		}

		_ = Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, fileName);
		File.WriteAllText(path, content);

		return path;
	}
}
