namespace Vexel.Telegram.Interactivity.Caching;

/// <summary>
/// Represents exclusive leased access to a piece of data. An asynchronous lock is held on the data while the lease is
/// active. The data is updated and the lock released when the lease is disposed.
/// </summary>
/// <typeparam name="TKey">The key type of the leased data.</typeparam>
/// <typeparam name="TData">The data type of the leased data.</typeparam>
public class DataLease<TKey, TData> : IAsyncDisposable where TKey : notnull
{
	private readonly InMemoryDataService<TKey, TData> _dataService;
	private readonly SemaphoreSlim _semaphore;

	private bool _shouldDelete;
	private bool _isDisposed;
	private TData _data;

	/// <summary>
	/// Gets the key associated with the lease.
	/// </summary>
	public TKey Key { get; }

	/// <summary>
	/// Gets or sets the data associated with the lease.
	/// </summary>
	public TData Data
	{
		get
		{
			ObjectDisposedException.ThrowIf(_isDisposed, "The data lease has expired.");
			return _data;
		}
		set
		{
			ObjectDisposedException.ThrowIf(_isDisposed, "The data lease has expired.");
			_data = value;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="DataLease{TKey, TData}"/> class.
	/// </summary>
	/// <param name="dataService">The data service.</param>
	/// <param name="key">The key associated with the data.</param>
	/// <param name="semaphore">The semaphore associated with the data.</param>
	/// <param name="data">The data.</param>
	internal DataLease(InMemoryDataService<TKey, TData> dataService, TKey key, SemaphoreSlim semaphore, TData data)
	{
		_dataService = dataService;
		_semaphore = semaphore;
		_data = data;

		Key = key;
	}

	/// <summary>
	/// Marks the leased data for deletion upon disposal.
	/// </summary>
	public void Delete()
	{
		_shouldDelete = true;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_isDisposed)
		{
			return;
		}

		GC.SuppressFinalize(this);

		try
		{
			if (_shouldDelete)
			{
				_ = await _dataService.TryDeleteDataAsync(this);
				return;
			}

			_dataService.UpdateData(Key, _data);
		}
		finally
		{
			_isDisposed = true;
			_ = _semaphore.Release();
		}
	}
}
