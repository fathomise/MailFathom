// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.TestSupport;

/// <summary>Answers where stored emails came from with provenance a test states, for suites that read mail into an AI turn or a citation.</summary>
/// <remarks>
/// A hand-written double rather than a substitute, because the reader's contract is about which emails it answers: an
/// email the test did not state is absent from the answer, exactly as one no longer stored is, so a suite can arrange a
/// message that vanished between its read and its provenance without writing a matcher for it.
/// </remarks>
internal sealed class StubEmailProvenanceReader : IEmailProvenanceReader
{
    private readonly Func<StoredEmailId, EmailProvenance?> provenanceOf;

    private StubEmailProvenanceReader(Func<StoredEmailId, EmailProvenance?> provenanceOf) =>
        this.provenanceOf = provenanceOf;

    /// <summary>Gets the provenance of an authenticated author the mailbox has corresponded with before, which asks a model to qualify nothing.</summary>
    public static EmailProvenance KnownCorrespondent { get; } = new()
    {
        Sender = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Unknown,
        },
        IsInJunkFolder = false,
        IsFirstContact = false,
    };

    /// <summary>Gets the provenance of a first-time sender nothing authenticated, sitting in the junk folder.</summary>
    public static EmailProvenance UnverifiedStranger { get; } = new()
    {
        Sender = SenderVerification.NotEstablished,
        IsInJunkFolder = true,
        IsFirstContact = true,
    };

    /// <summary>Gets a reader answering every email as written by <see cref="KnownCorrespondent" />.</summary>
    public static StubEmailProvenanceReader AllKnownCorrespondents { get; } = Answering(KnownCorrespondent);

    /// <summary>Builds a reader answering every email with the same provenance.</summary>
    /// <param name="provenance">The provenance every email carries.</param>
    /// <returns>The reader.</returns>
    public static StubEmailProvenanceReader Answering(EmailProvenance provenance) => new(_ => provenance);

    /// <summary>Builds a reader answering only the named emails, each with its own provenance.</summary>
    /// <param name="provenance">The provenance of each email the reader knows; any other is answered as no longer stored.</param>
    /// <returns>The reader.</returns>
    public static StubEmailProvenanceReader Answering(IReadOnlyDictionary<StoredEmailId, EmailProvenance> provenance) =>
        new(email => provenance.GetValueOrDefault(email));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<StoredEmailId, EmailProvenance>> ReadAsync(
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        IReadOnlyDictionary<StoredEmailId, EmailProvenance> answered = emails
            .Distinct()
            .Select(email => (Email: email, Provenance: this.provenanceOf(email)))
            .Where(entry => entry.Provenance is not null)
            .ToDictionary(entry => entry.Email, entry => entry.Provenance!);

        return Task.FromResult(answered);
    }
}
