namespace Vexel.Telegram.Handlers.Attributes;

/// <summary>
/// Marks an Immediate.Handlers handler as a CallbackQuery observer in the On* fan-out.
/// Requires a sibling <c>[Handler]</c> attribute (Option A dual-attr model).
/// </summary>
/// <remarks>
/// On* handlers always run after the routed handler for the same update, awaited sequentially
/// in fully-qualified metadata name order on the chat lane.
/// Request shape must be an empty record; inject <c>CallbackContext</c> / <c>Feedback</c> for payload access.
/// Fire-and-forget is deferred post-2.0; this attribute stays shape-compatible with a later opt-in.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OnCallbackQueryAttribute : Attribute;
