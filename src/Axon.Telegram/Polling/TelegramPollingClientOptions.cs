using Telegram.Bot.Polling;

namespace Axon.Telegram.Polling;

/// <summary>
/// Options for configuring the <see cref="TelegramPollingClient"/>.
/// </summary>
public sealed record TelegramPollingClientOptions
{
    /// <summary>
    /// Gets or sets the options to use for receiving updates from Telegram.
    /// If null, default options will be used by the Telegram.Bot library.
    /// </summary>
    public ReceiverOptions? ReceiverOptions { get; set; }
}