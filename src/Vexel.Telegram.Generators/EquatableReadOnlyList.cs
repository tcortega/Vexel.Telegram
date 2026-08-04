using System.Collections;

namespace Vexel.Telegram.Generators;

internal readonly struct EquatableReadOnlyList<T>(T[]? values) :
	IEquatable<EquatableReadOnlyList<T>>,
	IReadOnlyList<T>
	where T : IEquatable<T>
{
	private readonly T[] _values = values ?? [];

	public EquatableReadOnlyList(IEnumerable<T> values)
		: this([.. values])
	{
	}

	public static EquatableReadOnlyList<T> Empty { get; } = new([]);

	public T this[int index] => _values[index];

	public int Count => _values.Length;

	public bool Equals(EquatableReadOnlyList<T> other)
	{
		if (_values.Length != other._values.Length)
		{
			return false;
		}

		for (var i = 0; i < _values.Length; i++)
		{
			if (!_values[i].Equals(other._values[i]))
			{
				return false;
			}
		}

		return true;
	}

	public override bool Equals(object? obj) => obj is EquatableReadOnlyList<T> other && Equals(other);

	public override int GetHashCode()
	{
		unchecked
		{
			var hash = 17;
			foreach (var value in _values)
			{
				hash = (hash * 31) + (value?.GetHashCode() ?? 0);
			}

			return hash;
		}
	}

	public Enumerator GetEnumerator() => new(_values);

	IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();

	public struct Enumerator(T[] values)
	{
		private int _index = -1;

		public readonly T Current => values[_index];

		public bool MoveNext() => ++_index < values.Length;
	}
}
