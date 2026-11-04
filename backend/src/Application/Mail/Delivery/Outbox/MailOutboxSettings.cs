// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Bounds one delivery pass: how much it takes, how long it holds it, how long one send may run, and how often a transient failure is retried.</summary>
/// <remarks>
/// <para>
/// The ordering between the two durations is the whole reason this is validated rather than passed as loose values. An
/// attempt has to be cancelled before its lease can expire underneath it, because a lease that ran out while its holder
/// was still transmitting is a second attempt taking a message the first may already have sent — so a timeout at or
/// above the lease duration is refused rather than warned about.
/// </para>
/// <para>
/// The attempt bound, the two retry delays, and the retry window are the outbox's own budget for offering a message
/// again, and there is exactly one of them. What an attempt calls is already retried inside itself by the delivery dependency's resilience
/// pipeline, which repeats a submission server's explicit temporary rejection and nothing else; a second bound at this
/// level would multiply against that one instead of bounding anything.
/// </para>
/// </remarks>
public sealed record MailOutboxSettings
{
    private MailOutboxSettings(
        int maxDeliveriesPerPass,
        TimeSpan leaseDuration,
        TimeSpan attemptTimeout,
        int maxAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan retryMaxDelay,
        TimeSpan allowedLateness)
    {
        this.MaxDeliveriesPerPass = maxDeliveriesPerPass;
        this.LeaseDuration = leaseDuration;
        this.AttemptTimeout = attemptTimeout;
        this.MaxAttempts = maxAttempts;
        this.RetryBaseDelay = retryBaseDelay;
        this.RetryMaxDelay = retryMaxDelay;
        this.AllowedLateness = allowedLateness;
    }

    /// <summary>Gets the greatest number of queued sends one pass claims.</summary>
    /// <remarks>
    /// A send is a conversation with a submission server rather than a row to process, so the useful values are small.
    /// What the bound leaves is claimed by the next pass, oldest first, so nothing is dropped by it.
    /// </remarks>
    public int MaxDeliveriesPerPass { get; }

    /// <summary>Gets how long a claim holds each record it takes.</summary>
    public TimeSpan LeaseDuration { get; }

    /// <summary>Gets how long one send may be attempted for before it is cancelled.</summary>
    public TimeSpan AttemptTimeout { get; }

    /// <summary>Gets how many attempts one send may be handed out for before a transient failure ends it.</summary>
    /// <remarks>A value of <c>1</c> leaves no retry at all, so the first transient refusal is terminal.</remarks>
    public int MaxAttempts { get; }

    /// <summary>Gets the delay the first retry is drawn around, from which the doubling grows.</summary>
    public TimeSpan RetryBaseDelay { get; }

    /// <summary>Gets the ceiling a grown retry delay never exceeds.</summary>
    public TimeSpan RetryMaxDelay { get; }

    /// <summary>Gets how long after the time a send was written for this deployment will still deliver it.</summary>
    /// <remarks>
    /// It bounds nothing an ordinary send meets, because a send that named no time is never late. What it decides is
    /// the one case a held message reaches after the moment it was written for — a process that was down, a queue that
    /// was full, a provider that was unreachable for the whole window — where delivering and dropping are both wrong
    /// answers and the deployment says which side of them a message falls on.
    /// </remarks>
    public TimeSpan AllowedLateness { get; }

    /// <summary>Gets how long after its first failed attempt a send may still be attempted, which is the latest a person learns it is not going out.</summary>
    /// <remarks>
    /// <para>
    /// It bounds the retries beside <see cref="MaxAttempts" />, and whichever ends first ends the send: a message is
    /// time-sensitive, so a send refused for now or whose server cannot be reached is refused in the open once this has
    /// passed, and the person decides whether to send it again. The default is fifteen minutes.
    /// </para>
    /// <para>
    /// It is set beside the other bounds rather than taken by <see cref="Create" />, so every caller that has no reason
    /// to name it gets the default the product promises.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value set is not positive.</exception>
    public TimeSpan RetryWindow
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultRetryWindow;

    /// <summary>Gets the time a send may be retried for when a deployment names none.</summary>
    public static TimeSpan DefaultRetryWindow { get; } = TimeSpan.FromMinutes(15);

    /// <summary>Reports whether an attempt starting at an instant would still end inside the time a send may be retried for.</summary>
    /// <param name="firstFailedAt">When an attempt of the send first failed for a reason that could clear.</param>
    /// <param name="attemptStartsAt">When the attempt in question would begin.</param>
    /// <returns><see langword="true" /> when the attempt, run out to its whole budget, ends no later than the window closes.</returns>
    /// <remarks>
    /// The attempt's end is weighed rather than its start, because the refusal a person is told about is recorded when
    /// the last attempt ends: one that began inside the window and ran on past it would tell them late.
    /// </remarks>
    public bool AdmitsRetry(DateTimeOffset firstFailedAt, DateTimeOffset attemptStartsAt) =>
        attemptStartsAt + this.AttemptTimeout <= firstFailedAt + this.RetryWindow;

    /// <summary>States the bounds one pass runs under.</summary>
    /// <param name="maxDeliveriesPerPass">The greatest number of queued sends one pass claims.</param>
    /// <param name="leaseDuration">How long a claim holds each record it takes.</param>
    /// <param name="attemptTimeout">How long one send may be attempted for.</param>
    /// <param name="maxAttempts">How many attempts one send may be handed out for.</param>
    /// <param name="retryBaseDelay">The delay the first retry is drawn around.</param>
    /// <param name="retryMaxDelay">The ceiling a grown retry delay never exceeds.</param>
    /// <param name="allowedLateness">How long after the time a send was written for this deployment will still deliver it.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is not positive, when a duration is not positive, when <paramref name="attemptTimeout" /> is not shorter than <paramref name="leaseDuration" />, or when <paramref name="retryMaxDelay" /> is below <paramref name="retryBaseDelay" />.</exception>
    public static MailOutboxSettings Create(
        int maxDeliveriesPerPass,
        TimeSpan leaseDuration,
        TimeSpan attemptTimeout,
        int maxAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan retryMaxDelay,
        TimeSpan allowedLateness)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDeliveriesPerPass);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(attemptTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(attemptTimeout, leaseDuration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryBaseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryMaxDelay, retryBaseDelay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(allowedLateness, TimeSpan.Zero);

        return new MailOutboxSettings(
            maxDeliveriesPerPass,
            leaseDuration,
            attemptTimeout,
            maxAttempts,
            retryBaseDelay,
            retryMaxDelay,
            allowedLateness);
    }
}
