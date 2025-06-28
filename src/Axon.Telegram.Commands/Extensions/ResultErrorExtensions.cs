using Remora.Commands.Results;
using Remora.Results;

namespace Axon.Telegram.Commands.Extensions;

/// <summary>
/// Defines extension methods for the <see cref="IResultError"/> interface.
/// </summary>
public static class ResultErrorExtensions
{
    /// <summary>
    /// Determines whether the error has resulted from either invalid user input or the execution environment.
    /// </summary>
    /// <remarks>
    /// Currently, the following errors are considered user error.
    /// <list type="bullet">
    /// <item>CommandNotFoundError</item>
    /// <item>AmbiguousCommandInvocationError</item>
    /// <item>RequiredParameterValueMissingError</item>
    /// <item>ParameterParsingError</item>
    /// <item>ConditionNotSatisfiedError</item>
    /// </list>
    /// </remarks>
    /// <param name="error">The error.</param>
    /// <returns>true if the error is a user or environment error; otherwise, false.</returns>
    public static bool IsUserOrEnvironmentError(this IResultError error)
    {
        return error
            is CommandNotFoundError
            or AmbiguousCommandInvocationError
            or RequiredParameterValueMissingError
            or ParameterParsingError
            or ConditionNotSatisfiedError;
    }
}