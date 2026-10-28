// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Linq.Expressions;
using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The columns one email summary is built from, as PostgreSQL returns them.</summary>
/// <remarks>
/// <para>
/// The projection stops here rather than constructing the application read model directly, because a domain value
/// object's factory inside an <c>IQueryable</c> projection is either untranslatable or silently evaluated on the client.
/// Mapping outside the query keeps what PostgreSQL computes and what the process computes separable by reading.
/// </para>
/// <para>
/// It carries exactly the columns the summary carries and no others. Notably absent are the <c>Cc</c>, <c>Reply-To</c>,
/// and thread-reference arrays, which are filterable but not listed, every column of the raw MIME table, which no
/// summary query joins to at all, and the table holding the signer's key material, which nothing publishes. The
/// sender-authentication and signature columns are the two groups a listing reads without publishing all of them: the
/// single-email read is built from this same summary and publishes the evidence, and one projection reading them is
/// cheaper than a second query for the read that needs them. The spam columns are the one join:
/// five columns of the message's classification row, keyed by the message, and never its signals, its score, or its
/// profile, which say what the verdict was reached from rather than what a list draws.
/// </para>
/// <para>
/// The projection and the mapping live on the row rather than in either reader, because a listing and a single-email
/// lookup publish the same summary. Two copies of a twenty-column projection would drift, and the column a copy forgot
/// would be missing from one of the two paths only.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailSummaryRow(
    Guid Id,
    string MailboxAccountId,
    Guid FolderId,
    string? FolderAlias,
    Guid? ThreadId,
    string? InternetMessageId,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    long SizeOctets,
    string? SenderDisplayName,
    string? SenderAddress,
    string[] ToAddresses,
    int AttachmentCount,
    long AttachmentTotalSizeOctets,
    int InlineResourceCount,
    bool IsEncrypted,
    bool CarriesUnverifiedSignature,
    bool ContainsUnexpandedTnefPart,
    StoredEmailContentAvailability ContentAvailability,
    DateTimeOffset? RemoteFlagsObservedAt,
    bool IsRemotelySeen,
    bool IsRemotelyAnswered,
    bool IsRemotelyFlagged,
    bool IsRemotelyDraft,
    bool IsRemotelyDeleted,
    string[] RemoteKeywords,
    AuthorAuthenticationOutcome AuthorAuthenticationOutcome,
    AuthorAuthenticationRoute AuthorAuthenticationRoute,
    SenderTrustLevel SenderTrustLevel,
    string? AuthenticatedSenderDomain,
    string? DisplayedAuthorDomain,
    SenderAuthenticationMethod SenderAuthenticationMethod,
    DmarcOutcome DmarcOutcome,
    SenderAuthenticationSource SenderAuthenticationSource,
    string? DkimSignerDomain,
    string? SpfMailFromDomain,
    SenderTrustSource SenderTrustGrantedBy,
    Guid? SenderTrustOrganizationId,
    TrustedAuthorEvidence SenderTrustAuthorEvidence,
    Guid? SenderTrustContactId,
    MachineAuthorshipBand MachineAuthorshipBand,
    double MachineAuthorshipLikelihood,
    MachineAuthorshipSignals MachineAuthorshipSignals,
    string? MachineAuthorshipProfileRevision,
    StoredEmailSignatureRow Signature,
    SpamVerdict? SpamVerdict,
    SpamClassificationStage? SpamDecidedBy,
    SpamSubcategory? SpamSubcategory,
    SpamPhishingRequest? SpamPhishingRequest,
    SpamImpersonatedParty? SpamImpersonatedParty)
{
    /// <summary>Gets the projection every summary query selects, which is what keeps the two readers publishing one shape.</summary>
    public static Expression<Func<StoredEmailEntity, StoredEmailSummaryRow>> Projection { get; } = email =>
        new StoredEmailSummaryRow(
            email.Id,
            email.MailboxAccountId,
            email.FolderId,
            email.Folder.Alias,
            email.EmailThreadId,
            email.InternetMessageId,
            email.Subject,
            email.SentAt,
            email.ReceivedAt,
            email.SizeOctets,
            email.SenderDisplayName,
            email.SenderAddress,
            email.ToAddresses,
            email.AttachmentCount,
            email.AttachmentTotalSizeOctets,
            email.InlineResourceCount,
            email.IsEncrypted,
            email.CarriesUnverifiedSignature,
            email.ContainsUnexpandedTnefPart,
            email.ContentAvailability,
            email.RemoteFlagsObservedAt,
            email.IsRemotelySeen,
            email.IsRemotelyAnswered,
            email.IsRemotelyFlagged,
            email.IsRemotelyDraft,
            email.IsRemotelyDeleted,
            email.RemoteKeywords,
            email.AuthorAuthenticationOutcome,
            email.AuthorAuthenticationRoute,
            email.SenderTrustLevel,
            email.AuthenticatedSenderDomain,
            email.DisplayedAuthorDomain,
            email.SenderAuthenticationMethod,
            email.DmarcOutcome,
            email.SenderAuthenticationSource,
            email.DkimSignerDomain,
            email.SpfMailFromDomain,
            email.SenderTrustGrantedBy,
            email.SenderTrustOrganizationId,
            email.SenderTrustAuthorEvidence,
            email.SenderTrustContactId,
            email.MachineAuthorshipBand,
            email.MachineAuthorshipLikelihood,
            email.MachineAuthorshipSignals,
            email.MachineAuthorshipProfileRevision,
            new StoredEmailSignatureRow(
                email.SignatureFormat,
                email.SignatureSecureMimeType,
                email.SignatureUnverifiableReason,
                email.SignatureSignedAt,
                email.SignatureContentCheck,
                email.SignatureAuthorityCheck,
                email.SignatureTrustAnchor,
                email.SignatureChainEnd,
                email.SignatureValidityCheck,
                email.SignatureValidityFailure,
                email.SignatureRevocationCheck,
                email.SignatureRevocationMethod,
                email.SignatureRevocationCheckedAt,
                email.SignatureRevokedAt,
                email.SignatureSignerCheck,
                email.SignerName,
                email.SignerAddress,
                email.SignerIssuer,
                email.SignerValidFrom,
                email.SignerValidUntil,
                email.SignerSerialNumber,
                email.SignerFingerprint,
                email.SignerKeyId,
                email.SignerPublicKeyAlgorithm,
                email.SignerDigestAlgorithm),
            email.SpamClassification == null ? null : email.SpamClassification.Verdict,
            email.SpamClassification == null ? null : email.SpamClassification.DecidedBy,
            email.SpamClassification == null ? null : email.SpamClassification.SpamSubcategory,
            email.SpamClassification == null ? null : email.SpamClassification.PhishingRequest,
            email.SpamClassification == null ? null : email.SpamClassification.ImpersonatedParty);

    /// <summary>Rebuilds the summaries of a set of rows, naming each one's folder by its path and the organization that recognized its author by its current name.</summary>
    /// <param name="rows">The rows, in the order the summaries are wanted in.</param>
    /// <param name="dbContext">The context the folder paths and the organization names are read through, untracked.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One summary per row, in the same order.</returns>
    /// <remarks>
    /// <para>
    /// The paths are one more query, over the folders of the accounts the rows came from, rather than columns of the
    /// summary projection: a path is a walk up a parent chain of any depth, which no single translated expression reads.
    /// </para>
    /// <para>
    /// The organization names are another, asked only where a row names an organization, so a page of mail nobody's
    /// organization signed costs nothing more. The name is read live rather than copied onto the message, which is what
    /// lets a rename reach mail already stored and a deletion leave the reason standing without one.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<EmailSummary>> ToSummariesAsync(
        IReadOnlyCollection<StoredEmailSummaryRow> rows,
        MailFathomDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(dbContext);

        var paths = await StoredMailFolderPaths.ReadAsync(
            dbContext.MailFolders,
            [.. rows.Select(static row => row.MailboxAccountId)],
            cancellationToken);
        var organizationNames = await ReadOrganizationNamesAsync(dbContext.Organizations, rows, cancellationToken);

        return [.. rows.Select(row => row.ToSummary(paths, organizationNames))];
    }

    /// <summary>Turns the returned columns into the application read model.</summary>
    /// <param name="folderPaths">The path of every folder of the row's account, keyed by folder identity, which names the row's folder.</param>
    /// <param name="organizationNames">The current display name of every organization the rows name, keyed by its identity; one deleted since is absent.</param>
    /// <returns>The summary, with every domain value object built by its own factory.</returns>
    internal EmailSummary ToSummary(
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> folderPaths,
        IReadOnlyDictionary<Guid, string> organizationNames) => new()
        {
            StoredEmailId = StoredEmailId.Create(this.Id),
            Account = MailAccountId.Create(this.MailboxAccountId),
            Folder = StoredMailFolderPaths.Describe(this.FolderId, this.FolderAlias, folderPaths),
            ThreadId = this.ThreadId is { } threadId ? EmailThreadId.Create(threadId) : null,
            InternetMessageId = this.InternetMessageId,
            Subject = this.Subject,
            SentAt = this.SentAt,
            ReceivedAt = this.ReceivedAt,
            SizeOctets = this.SizeOctets,
            SenderDisplayName = this.SenderDisplayName,
            SenderAddress = this.SenderAddress,
            // A read-only view rather than the array itself, which a caller could cast back and write through.
            ToAddresses = Array.AsReadOnly(this.ToAddresses),
            Attachments = new StoredEmailAttachmentSummary(
                this.AttachmentCount,
                this.AttachmentTotalSizeOctets,
                this.InlineResourceCount,
                this.IsEncrypted,
                this.CarriesUnverifiedSignature,
                this.ContainsUnexpandedTnefPart),
            ContentAvailability = this.ContentAvailability,
            RemoteFlags = new RemoteEmailFlagSnapshot(
                this.RemoteFlagsObservedAt,
                this.IsRemotelySeen,
                this.IsRemotelyAnswered,
                this.IsRemotelyFlagged,
                this.IsRemotelyDraft,
                this.IsRemotelyDeleted,
                // Rebuilt through the factory rather than wrapped, because the column is what an earlier build's
                // normalization left behind and this one's rules are the ones a caller's filter is folded by.
                RemoteEmailKeywords.Create(this.RemoteKeywords)),
            SenderVerification = new SenderVerification
            {
                AuthorAuthentication = this.AuthorAuthenticationOutcome,
                DeploymentTrust = this.SenderTrustLevel,
                DeploymentTrustGrantedBy = this.SenderTrustGrantedBy,
                DeploymentTrustAuthorEvidence = this.SenderTrustAuthorEvidence,
                RecognizingContactId = this.SenderTrustContactId is { } contactId && contactId != Guid.Empty
                    ? ContactId.Create(contactId)
                    : null,
                RecognizingOrganizationName = this.SenderTrustOrganizationId is { } organizationId
                    ? organizationNames.GetValueOrDefault(organizationId)
                    : null,
                Signature = this.Signature.ToSignature(),
            },
            SenderAuthenticationEvidence = new SenderAuthenticationEvidence
            {
                AuthenticatedDomain = StoredDomain(this.AuthenticatedSenderDomain),
                DisplayedAuthorDomain = StoredDomain(this.DisplayedAuthorDomain),
                DkimDomain = StoredDomain(this.DkimSignerDomain),
                SpfDomain = StoredDomain(this.SpfMailFromDomain),
                AuthorRoute = this.AuthorAuthenticationRoute,
                AuthenticatedBy = this.SenderAuthenticationMethod,
                Dmarc = this.DmarcOutcome,
                Source = this.SenderAuthenticationSource,
            },
            MachineAuthorship = this.StoredAuthorship(),
            Spam = this.StoredSpamVerdict(),
        };

    /// <summary>Reads the current display name of every organization the rows name.</summary>
    private static async Task<IReadOnlyDictionary<Guid, string>> ReadOrganizationNamesAsync(
        IQueryable<OrganizationEntity> organizations,
        IReadOnlyCollection<StoredEmailSummaryRow> rows,
        CancellationToken cancellationToken)
    {
        Guid[] organizationIds = [.. rows
            .Select(static row => row.SenderTrustOrganizationId)
            .OfType<Guid>()
            .Distinct()];

        if (organizationIds.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await organizations
            .AsNoTracking()
            .Where(organization => organizationIds.Contains(organization.Id))
            .Select(static organization => new { organization.Id, organization.DisplayName })
            .ToDictionaryAsync(
                static organization => organization.Id,
                static organization => organization.DisplayName,
                cancellationToken);
    }

    /// <summary>Reads the classification columns back into the verdict a screen draws, or reports that none is recorded.</summary>
    /// <remarks>
    /// The verdict and the deciding stage are columns of one row, absent together exactly when the message has no
    /// classification, which is the only case read as none.
    /// </remarks>
    private EmailSpamVerdict? StoredSpamVerdict() =>
        this is { SpamVerdict: { } verdict, SpamDecidedBy: { } decidedBy }
            ? new EmailSpamVerdict(
                verdict,
                decidedBy,
                this.SpamSubcategory,
                this.SpamPhishingRequest,
                this.SpamImpersonatedParty)
            : null;

    /// <summary>Reads the authorship columns back into the assessment they were written from.</summary>
    /// <remarks>
    /// The band is what says whether a reading happened, so it decides which of the two shapes the row holds rather
    /// than the revision or the likelihood: a row nothing assessed carries the not-assessed state whole, and a row a
    /// profile read carries what that profile found. A likelihood the column somehow holds outside the scale is read
    /// as zero rather than raised over, for the reason an unusable domain column is read as absent — the alternative is
    /// a stored email that can never be listed again, and nothing writes one, because the column is written from the
    /// same record this rebuilds.
    /// </remarks>
    private MachineAuthorshipAssessment StoredAuthorship() =>
        this.MachineAuthorshipBand == MachineAuthorshipBand.NotAssessed
            ? MachineAuthorshipAssessment.NotAssessed
            : MachineAuthorshipAssessment.Assessed(
                this.MachineAuthorshipBand,
                StoredLikelihood(this.MachineAuthorshipLikelihood),
                this.MachineAuthorshipSignals,
                // Qualified because the column and the value object it is read into carry the same name, which is what
                // keeps the row's four authorship parameters reading as one group; the property would otherwise shadow
                // the type at this one call site.
                MailFathom.Domain.Emails.Authorship.MachineAuthorshipProfileRevision.FromStoredValue(
                    this.MachineAuthorshipProfileRevision));

    /// <summary>Reads a stored likelihood, or reports that the column holds no usable one.</summary>
    /// <remarks>
    /// Outside the scale is read as zero rather than clamped to the nearest end, because clamping upwards would turn a
    /// column nobody can account for into the strongest reading this feature publishes.
    /// </remarks>
    private static double StoredLikelihood(double likelihood) =>
        double.IsFinite(likelihood) && likelihood is >= 0 and <= 1 ? likelihood : 0;

    /// <summary>Reads one stored domain column back into its value object, or reports that the column holds none.</summary>
    /// <remarks>
    /// A column value the factory refuses is read as absent rather than raised, because the alternative is a stored
    /// email that can never be listed again. Nothing writes one: the columns are written from the same value object,
    /// and its own bound is what the column length was taken from.
    /// </remarks>
    private static SenderDomain? StoredDomain(string? domain) =>
        domain is not null && SenderDomain.TryCreate(domain, out var stored) ? stored : null;
}
