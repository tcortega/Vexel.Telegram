namespace Vexel.Telegram.Generators;

internal static class CommandNameValidation
{
	public static bool IsValid(string name)
	{
		if (name.Length is < 1 or > 32)
		{
			return false;
		}

		foreach (var c in name)
		{
			if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
			{
				continue;
			}

			return false;
		}

		return true;
	}
}
