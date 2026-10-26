// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Holds each certificate's revocation answer for as long as the answer says it stays valid.</summary>
/// <remarks>
/// <para>
/// A mailbox receives many signed messages from few certificates, so a re-derivation over a whole mailbox asks each
/// responder once per certificate rather than once per message. An OCSP response and a CRL both say until when they
/// hold — their <c>nextUpdate</c> — and that is how long the answer is kept, clamped so a responder publishing a very
/// short or very long lifetime is neither asked per message nor believed for a week.
/// </para>
/// <para>
/// An answer of "could not be established" is kept too, briefly, so a responder that is down is not asked again for every
/// message signed with the same certificate in one run, while one that recovers is believed again a minute later.
/// </para>
/// <para>
/// It is bounded in entries because the key comes from mail that arrives, and reaching the bound empties it rather than
/// evicting by an order nothing here needs, as the DKIM record cache does. It belongs to one replica: each replica asks
/// on its own, which multiplies the requests by the replica count and changes no answer.
/// </para>
/// </remarks>
internal sealed class CertificateRevocationAnswerCache
{
    /// <summary>How many certificates are held before the cache starts over.</summary>
    private const int MaximumEntries = 2048;

    /// <summary>The shortest time an answer is held, whatever a shorter <c>nextUpdate</c> would ask for.</summary>
    private static readonly TimeSpan ShortestLifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest time an answer is held, whatever a later <c>nextUpdate</c> would permit.</summary>
    private static readonly TimeSpan LongestLifetime = TimeSpan.FromDays(1);

    /// <summary>How long an answer that could not be established is remembered.</summary>
    private static readonly TimeSpan UnavailableLifetime = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, CachedAnswer> answersByCertificate = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes an empty cache over the clock its lifetimes are measured against.</summary>
    /// <param name="timeProvider">Supplies the current instant.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public CertificateRevocationAnswerCache(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Reads what is held for one certificate, when something still is.</summary>
    /// <param name="certificateKey">The certificate's issuer key and serial number, as the checker names them.</param>
    /// <param name="answer">The held answer.</param>
    /// <returns><see langword="true" /> when an unexpired answer was held; otherwise <see langword="false" />.</returns>
    public bool TryRead(string certificateKey, out CertificateRevocationAnswer answer)
    {
        answer = CertificateRevocationAnswer.Unavailable;

        if (!this.answersByCertificate.TryGetValue(certificateKey, out var cached)
            || cached.ExpiresAt <= this.timeProvider.GetUtcNow())
        {
            return false;
        }

        answer = cached.Answer;

        return true;
    }

    /// <summary>Holds an answer until the instant it said it stays valid, within the clamp.</summary>
    /// <param name="certificateKey">The certificate's issuer key and serial number, as the checker names them.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="validUntil">The answer's own <c>nextUpdate</c>, absent where it stated none.</param>
    public void Store(string certificateKey, CertificateRevocationAnswer answer, DateTimeOffset? validUntil)
    {
        var now = this.timeProvider.GetUtcNow();
        var lifetime = answer.Status == CertificateRevocationStatus.Unavailable
            ? UnavailableLifetime
            : Clamped((validUntil ?? now) - now);

        if (this.answersByCertificate.Count >= MaximumEntries && !this.answersByCertificate.ContainsKey(certificateKey))
        {
            this.answersByCertificate.Clear();
        }

        this.answersByCertificate[certificateKey] = new CachedAnswer(answer, now + lifetime);
    }

    private static TimeSpan Clamped(TimeSpan lifetime) =>
        lifetime < ShortestLifetime ? ShortestLifetime
        : lifetime > LongestLifetime ? LongestLifetime
        : lifetime;

    /// <summary>One held answer and when it stops being believed.</summary>
    private readonly record struct CachedAnswer(CertificateRevocationAnswer Answer, DateTimeOffset ExpiresAt);
}
