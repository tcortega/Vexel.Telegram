namespace Vexel.Telegram.Handlers;

/// <summary>
/// Runtime knobs for conversation flows.
/// </summary>
public sealed class FlowOptions
{
	/// <summary>Default time-to-live for a newly armed step. Default: 15 minutes.</summary>
	public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(15);

	/// <summary>
	/// Generic reply sent when an armed step handler throws. Never includes exception details.
	/// Set <see cref="SendStepErrorReply"/> to <see langword="false"/> to suppress.
	/// </summary>
	public string StepErrorMessage { get; set; } =
		"Something went wrong - try again, or /cancel.";

	/// <summary>
	/// When <see langword="true"/> (default), the router sends <see cref="StepErrorMessage"/>
	/// via Feedback after a step-handler exception.
	/// </summary>
	public bool SendStepErrorReply { get; set; } = true;

	/// <summary>Confirmation text for the built-in <c>/cancel</c> command.</summary>
	public string CancelConfirmationMessage { get; set; } = "Cancelled.";
}
