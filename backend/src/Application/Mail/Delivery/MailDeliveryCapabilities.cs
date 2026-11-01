// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery;

/// <summary>States what a submission server said it will accept, as facts rather than as advertised extension names.</summary>
/// <param name="MaxMessageBytes">
/// The largest message the server declared it will accept, or <see langword="null" /> when it declared no bound. A
/// server that advertises the size extension without a number is stating that it has no fixed maximum, which is the
/// same absence.
/// </param>
/// <param name="AcceptsEightBitContent">Whether the server accepts content whose bytes are not restricted to seven bits, which decides whether it can carry an address outside ASCII; every body is sent seven-bit regardless.</param>
/// <param name="AcceptsInternationalizedAddresses">Whether the server accepts an envelope address outside ASCII.</param>
/// <remarks>
/// <para>
/// These three decide whether a message can be sent at all rather than how it is sent, which is why they are the ones
/// a session reports. A message beyond the declared size is refused after its whole body has crossed the network, and
/// an internationalized address the server cannot carry — which takes both of the other two answers — is refused
/// outright. A caller left to guess at either pays for the guess in a transmission that was never going to be accepted.
/// </para>
/// <para>
/// Everything else a server advertises is absent from the three on purpose. Pipelining and chunking change how a
/// submission is carried out and never whether it may happen, so they belong to whatever issues the commands rather than
/// to a contract every caller above reads. Delivery status notifications decide nothing about whether a message is sent
/// either, but a send that asked for one records whether the request went out with it, so
/// <see cref="AcceptsDeliveryStatusNotifications" /> reports them beside the three.
/// </para>
/// </remarks>
public sealed record MailDeliveryCapabilities(
    long? MaxMessageBytes,
    bool AcceptsEightBitContent,
    bool AcceptsInternationalizedAddresses)
{
    /// <summary>What a message is composed against when it is written down long before a server is opened to carry it.</summary>
    /// <remarks>
    /// <para>
    /// A send is recorded by one act and transmitted by another, so the message exists before any submission server has
    /// been asked anything. This is what the composition is held to in that gap, and each of the three values is the
    /// answer that stays correct whatever the server turns out to say.
    /// </para>
    /// <para>
    /// No size is declared, because the deployment's own bound is checked at composition regardless and the server's is
    /// checked against the stored length before the message is offered — so assuming a number here would refuse a
    /// message a server would have taken without protecting one it would not.
    /// </para>
    /// <para>
    /// Neither eight-bit content nor internationalized addresses are assumed, and together they are a refusal rather
    /// than a cost — the body is seven-bit whatever a server says, so only an address depends on them: an address
    /// outside ASCII is refused while the caller is still there to be told, instead of being queued and failing against
    /// the server hours later with nobody to answer for it.
    /// </para>
    /// </remarks>
    public static MailDeliveryCapabilities BeforeAnyServerHasSpoken { get; } = new(
        MaxMessageBytes: null,
        AcceptsEightBitContent: false,
        AcceptsInternationalizedAddresses: false);

    /// <summary>Gets whether the server advertised <c>DSN</c>, without which a message goes out with no delivery-notification request in its envelope.</summary>
    /// <remarks>
    /// It is not among the values a message is composed against, because the request is envelope data rather than part of
    /// the message: a server that does not carry it still takes the message, and the send records that the request was
    /// not carried rather than being refused.
    /// </remarks>
    public bool AcceptsDeliveryStatusNotifications { get; init; }

    /// <summary>Reports whether a message of the supplied size is within what the server declared it will accept.</summary>
    /// <param name="messageBytes">The size of the message as it would be transmitted.</param>
    /// <returns><see langword="true" /> when the server declared no bound or the message is within it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="messageBytes" /> is negative.</exception>
    /// <remarks>
    /// The comparison is inclusive, because the size extension states the largest message that is accepted rather than
    /// the first one that is not. A server declaring no bound permits every size here and may still refuse a message
    /// for a reason of its own; this answers what the server said, not what it will do.
    /// </remarks>
    public bool PermitsMessageOfSize(long messageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageBytes);

        return this.MaxMessageBytes is not { } declaredMaximum || messageBytes <= declaredMaximum;
    }
}
