using Microsoft.Extensions.DependencyInjection;
using Remora.Results;

namespace Axon.Telegram.Commands.Services.Execution;

/// <summary>
/// Collects execution event services for simpler conjoined execution.
/// This implementation is a direct adaptation of the service from Remora.Discord.Commands.
/// </summary>
public class ExecutionEventCollectorService
{
    /// <summary>
    /// Runs all collected post-execution events.
    /// </summary>
    /// <remarks>If no post-execution events are registered, this method will just return the original result.</remarks>
    /// <param name="services">The scoped service provider for this execution.</param>
    /// <param name="commandContext">The command context.</param>
    /// <param name="commandResult">The result of the executed command.</param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<Result> RunPostExecutionEventsAsync
    (
        IServiceProvider services,
        ICommandContext commandContext,
        IResult commandResult,
        CancellationToken ct
    )
    {
        var events = services.GetServices<IPostExecutionEvent>().ToArray();

        // If there are no handlers, just return the original result.
        if (events.Length <= 0)
        {
            return commandResult.IsSuccess
                ? Result.FromSuccess()
                : Result.FromError(commandResult.Error);
        }

        return await RunEventsAsync
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

    private static async Task<Result> RunEventsAsync
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