using Axon.Telegram.Commands.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Remora.Results;

namespace Axon.Telegram.Commands.Execution;

/// <summary>
/// Collects execution event services for simpler conjoined execution.
/// </summary>
public static class ExecutionEventCollectorService
{
	/// <summary>
	/// Runs all collected command preparation error events.
	/// </summary>
	/// <remarks>If no error events are registered, this method will just return the original result.</remarks>
	/// <param name="services">The service provider.</param>
	/// <param name="operationContext">The operation context.</param>
	/// <param name="preparationResult">The result of the command preparation.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	public static async Task<Result> RunPreparationErrorEvents
	(
		IServiceProvider services,
		IOperationContext operationContext,
		IResult preparationResult,
		CancellationToken ct
	)
	{
		var events = services.GetServices<IPreparationErrorEvent>().ToArray();
		if (events.Length <= 0)
		{
			return preparationResult.IsSuccess
				? Result.FromSuccess()
				: Result.FromError(preparationResult.Error);
		}

		return await RunEvents
		(
			events.Select
			(e => new Func<CancellationToken, Task<Result>>
				(token => e.PreparationFailed
					(
						operationContext,
						preparationResult,
						token
					)
				)
			),
			ct
		);
	}

	/// <summary>
	/// Runs all collected pre-execution events.
	/// </summary>
	/// <param name="services">The service provider.</param>
	/// <param name="commandContext">The command context.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	public static async Task<Result> RunPreExecutionEvents
	(
		IServiceProvider services,
		ICommandContext commandContext,
		CancellationToken ct
	)
	{
		var events = services.GetServices<IPreExecutionEvent>();
		return await RunEvents
		(
			events.Select
			(e => new Func<CancellationToken, Task<Result>>
				(token => e.BeforeExecutionAsync
					(
						commandContext,
						token
					)
				)
			),
			ct
		);
	}

	/// <summary>
	/// Runs all collected post-execution events.
	/// </summary>
	/// <remarks>If no post-execution events are registered, this method will just return the original result.</remarks>
	/// <param name="services">The service provider.</param>
	/// <param name="commandContext">The command context.</param>
	/// <param name="commandResult">The result of the executed command.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	public static async Task<Result> RunPostExecutionEvents
	(
		IServiceProvider services,
		ICommandContext commandContext,
		IResult commandResult,
		CancellationToken ct
	)
	{
		var events = services.GetServices<IPostExecutionEvent>().ToArray();
		if (events.Length <= 0)
		{
			return commandResult.IsSuccess
				? Result.FromSuccess()
				: Result.FromError(commandResult.Error);
		}

		return await RunEvents
		(
			events.Select
			(e => new Func<CancellationToken, Task<Result>>
				(token => e.AfterExecutionAsync
					(
						commandContext,
						commandResult,
						token
					)
				)
			),
			ct
		);
	}

	private static async Task<Result> RunEvents
	(
		IEnumerable<Func<CancellationToken, Task<Result>>> events,
		CancellationToken ct
	)
	{
		var errors = new List<Result>();

		foreach (var eventToExecute in events)
		{
			try
			{
				var result = await eventToExecute(ct);
				if (!result.IsSuccess)
				{
					errors.Add(result);
				}
			}
			catch (Exception e)
			{
				errors.Add(Result.FromError(new ExceptionError(e)));
			}
		}

		if (errors.Count > 0)
		{
			return errors.Count == 1
				? errors[0]
				: new AggregateError(errors.Cast<IResult>().ToList());
		}

		return Result.FromSuccess();
	}
}
