using System.Collections.Concurrent;
using Remora.Results;

namespace Axon.Telegram.Interactivity.Caching;

/// <summary>
/// Manages synchronized access to data.
/// </summary>
/// <remarks>
/// While <typeparamref name="TKey"/> should be a simple dictionary key, <typeparamref name="TData"/> may be any complex
/// type such as classes, structures, or records. If the data type implements <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/>, the instance will also be disposed upon removal.
/// </remarks>
/// <typeparam name="TKey">The key type for the stored data.</typeparam>
/// <typeparam name="TData">The data type stored by the service.</typeparam>
public class InMemoryDataService<TKey, TData> : IAsyncDisposable where TKey : notnull
{
	/// <summary>
	/// Gets the singleton instance of this service.
	/// </summary>
	public static InMemoryDataService<TKey, TData> Instance { get; } = new();

	private readonly ConcurrentDictionary<TKey, (SemaphoreSlim Semaphore, TData Data)> _data = new();
	private bool _isDisposed;

	private InMemoryDataService()
	{
	}

	/// <summary>
	/// Inserts a new data object into the service.
	/// </summary>
	/// <remarks>
	/// After inserting the value, any further access to the data is invalid. If you come from a C++ or Rust background,
	/// consider the value as having been moved into this method.
	/// </remarks>
	/// <param name="key">The key the data object is associated with.</param>
	/// <param name="data">The data object.</param>
	/// <returns>true if the data was successfully added; otherwise, false.</returns>
	public bool TryAddData(TKey key, TData data)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, "The data service has been disposed of and cannot be used.");

		return _data.TryAdd(key, (new SemaphoreSlim(1, 1), data));
	}

	/// <summary>
	/// Leases the data associated with the given key, blocking until a lock can be taken on the data object.
	/// </summary>
	/// <remarks>
	/// The semaphore returned by this method has the lock held on it and must be released once the caller is done with
	/// the object.
	/// </remarks>
	/// <param name="key">The key the data object is associated with.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>The data and semaphore associated with the data or a <see cref="NotFoundError"/>.</returns>
	public async Task<Result<DataLease<TKey, TData>>> LeaseDataAsync(TKey key, CancellationToken ct = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, "The data service has been disposed of and cannot be used.");

		if (!_data.TryGetValue(key, out var tuple))
		{
			return new NotFoundError();
		}

		var (semaphore, data) = tuple;
		await semaphore.WaitAsync(ct);

		// may have been deleted while we were waiting - check first
		if (_data.ContainsKey(key))
		{
			return new DataLease<TKey, TData>(this, key, semaphore, data);
		}

		_ = semaphore.Release();
		return new NotFoundError();
	}

	/// <summary>
	/// Deletes the data associated with the given lease.
	/// </summary>
	/// <param name="lease">The lease you have on the data object.</param>
	/// <returns>true if the data was successfully removed; otherwise, false.</returns>
	public bool TryDeleteData(DataLease<TKey, TData> lease)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, "The data service has been disposed of and cannot be used.");

		if (!_data.TryRemove(lease.Key, out _))
		{
			// already removed
			return false;
		}

		if (lease.Data is IAsyncDisposable and not IDisposable)
		{
			throw new InvalidOperationException
			(
				$"Unable to synchronously dispose of the held data belonging to key {lease.Key}."
			);
		}

		if (lease.Data is IDisposable disposable)
		{
			disposable.Dispose();
		}

		return true;
	}

	/// <summary>
	/// Deletes the data associated with the given lease.
	/// </summary>
	/// <param name="lease">The lease you have on the data object.</param>
	/// <returns>true if the data was successfully removed; otherwise, false.</returns>
	public async Task<bool> TryDeleteDataAsync(DataLease<TKey, TData> lease)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, "The data service has been disposed of and cannot be used.");

		if (!_data.TryRemove(lease.Key, out _))
		{
			// already removed
			return false;
		}

		switch (lease.Data)
		{
			// preferentially use the asynchronous disposal logic
			case IAsyncDisposable asyncDisposable:
			{
				await asyncDisposable.DisposeAsync();
				break;
			}
			case IDisposable disposable:
			{
				disposable.Dispose();
				break;
			}

			default:
				break;
		}

		return true;
	}

	/// <summary>
	/// Deletes the data associated with the key, disposing of the data if necessary. A lock is acquired before the data
	/// is disposed.
	/// </summary>
	/// <param name="key">The key the data object is associated with.</param>
	/// <returns>true if the data was successfully removed; otherwise, false.</returns>
	internal async ValueTask<bool> DeleteDataAsync(TKey key)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, "The data service has been disposed of and cannot be used.");

		if (!_data.TryRemove(key, out var tuple))
		{
			return false;
		}

		var (semaphore, data) = tuple;
		await semaphore.WaitAsync();

		switch (data)
		{
			case IAsyncDisposable asyncDisposableData:
			{
				await asyncDisposableData.DisposeAsync();
				break;
			}
			case IDisposable disposableData:
			{
				disposableData.Dispose();
				break;
			}

			default:
				break;
		}

		return true;
	}

	/// <summary>
	/// Updates the data identified by the given key.
	/// </summary>
	/// <remarks>
	/// The semaphore is not released by this method.</remarks>
	/// <param name="key">The key.</param>
	/// <param name="newData">The new data.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the key is not found or the semaphore is not held.
	/// </exception>
	internal void UpdateData(TKey key, TData newData)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, "The data service has been disposed of and cannot be used.");

		if (!_data.TryGetValue(key, out var existingData))
		{
			throw new InvalidOperationException("No data with that key could be found.");
		}

		var (existingSemaphore, _) = existingData;
		if (existingSemaphore.CurrentCount != 0)
		{
			throw new InvalidOperationException("The semaphore is not currently held, and you do not own the data.");
		}

		if (!_data.TryUpdate(key, (existingSemaphore, newData), existingData))
		{
			throw new InvalidOperationException
			(
				"Something updated the data while we were working on it. Concurrency bug?"
			);
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_isDisposed)
		{
			return;
		}

		GC.SuppressFinalize(this);

		var keys = _data.Keys.ToList();
		foreach (var key in keys)
		{
			_ = await DeleteDataAsync(key);
		}

		_isDisposed = true;
	}
}
