// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the provenance query PostgreSQL answers: the junk flag and whether a sender corresponded before.</summary>
/// <remarks>
/// <para>
/// Whether a sender is a first contact is one existence test per email, correlated on the email's own account, sender,
/// arrival instant, and message identifier, and filtered on the role of a joined folder and on containment in two array
/// columns. Each of those is a translation a provider could get wrong without throwing, and every way of getting it
/// wrong answers a plausible boolean — so the cases below are arranged so that each rule, broken alone, flips the answer
/// for exactly one email.
/// </para>
/// <para>
/// Every test seeds two mailbox accounts of its own, written straight through the persistence session rather than by a
/// synchronization run, because the query reads only the stored columns and the folder roles and a fresh account is the
/// one place a junk, trash, and drafts folder can be given their roles without taking them from another class's folders.
/// Nothing here is served or bound, so no other class's reads reach these rows, and they stay behind like the rest of
/// the run's data.
/// </para>
/// <para>
/// Those accounts are no account the harness serves, so the reader is composed here over the scoped context and a
/// folder port admitting each seeded account's mapped folder, rather than resolved with the harness's own port, which
/// would admit none of them and leave every sender a first contact.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailProvenanceReaderTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset Earliest = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Earlier = Earliest.AddHours(1);

    private static readonly DateTimeOffset Latest = Earliest.AddHours(2);

    /// <summary>
    /// A sender corresponded before when the same mailbox, outside junk, trash, and drafts, holds earlier mail they
    /// wrote or that was addressed to them in <c>To</c> or <c>Cc</c>; mail of another account and a sender with no
    /// address count for nothing. The sender's earlier mail sits in a folder no role names, which is the folder most mail
    /// is kept in and the one a role comparison that forgot a null would quietly drop.
    /// </summary>
    [Fact]
    public async Task ReadAsync_EarlierMailOfTheSender_CountsOnlyWhatTheSameMailboxKeepsOutsideJunkTrashAndDrafts()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var mailbox = new SeededMailbox();
        var elsewhere = new SeededMailbox();

        mailbox.Add(folder: null, "wrote@provenance.test", Earliest);
        mailbox.Add(MailFolderSpecialUse.Sent, "owner@provenance.test", Earliest, to: ["addressed@provenance.test"]);
        mailbox.Add(MailFolderSpecialUse.Sent, "owner@provenance.test", Earliest, cc: ["copied@provenance.test"]);
        mailbox.Add(MailFolderSpecialUse.Junk, "quarantined@provenance.test", Earliest);
        mailbox.Add(MailFolderSpecialUse.Trash, "quarantined@provenance.test", Earliest);
        mailbox.Add(MailFolderSpecialUse.Drafts, "owner@provenance.test", Earliest, to: ["quarantined@provenance.test"]);
        elsewhere.Add(MailFolderSpecialUse.Inbox, "elsewhere@provenance.test", Earliest);

        var wrote = mailbox.Add(MailFolderSpecialUse.Inbox, "wrote@provenance.test", Latest);
        var addressed = mailbox.Add(MailFolderSpecialUse.Inbox, "addressed@provenance.test", Latest);
        var copied = mailbox.Add(MailFolderSpecialUse.Inbox, "copied@provenance.test", Latest);
        var quarantined = mailbox.Add(MailFolderSpecialUse.Inbox, "quarantined@provenance.test", Latest);
        var fromElsewhere = mailbox.Add(MailFolderSpecialUse.Inbox, "elsewhere@provenance.test", Latest);
        var anonymous = mailbox.Add(MailFolderSpecialUse.Inbox, sender: null, Latest);

        await SeedAsync(services, [mailbox, elsewhere], cancellationToken);

        // Act
        var provenance = await ReadAsync(
            services,
            [mailbox, elsewhere],
            [wrote, addressed, copied, quarantined, fromElsewhere, anonymous],
            cancellationToken);

        // Assert
        Assert.False(provenance[wrote].IsFirstContact);
        Assert.False(provenance[addressed].IsFirstContact);
        Assert.False(provenance[copied].IsFirstContact);
        Assert.True(provenance[quarantined].IsFirstContact);
        Assert.True(provenance[fromElsewhere].IsFirstContact);
        Assert.True(provenance[anonymous].IsFirstContact);
    }

    /// <summary>
    /// Earlier means received strictly before, so a later message is no correspondence for an earlier one and a copy of
    /// one message in two folders is none for either; an email whose arrival is unknown counts any other mail of the
    /// sender except a copy carrying its own <c>Message-ID</c>.
    /// </summary>
    [Fact]
    public async Task ReadAsync_MailOfTheSenderAroundTheEmail_CountsOnlyWhatArrivedStrictlyBeforeIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var mailbox = new SeededMailbox();

        var firstOfTwo = mailbox.Add(MailFolderSpecialUse.Inbox, "twice@provenance.test", Earlier);
        var secondOfTwo = mailbox.Add(MailFolderSpecialUse.Inbox, "twice@provenance.test", Latest);
        var copyInInbox = mailbox.Add(
            MailFolderSpecialUse.Inbox,
            "copied-across@provenance.test",
            Earlier,
            messageId: "copied-across@provenance.test");
        var copyInArchive = mailbox.Add(
            MailFolderSpecialUse.Archive,
            "copied-across@provenance.test",
            Earlier,
            messageId: "copied-across@provenance.test");

        mailbox.Add(MailFolderSpecialUse.Archive, "undated@provenance.test", Earlier, messageId: "undated@provenance.test");
        var undated = mailbox.Add(
            MailFolderSpecialUse.Inbox,
            "undated@provenance.test",
            receivedAt: null,
            messageId: "undated@provenance.test");

        mailbox.Add(MailFolderSpecialUse.Archive, "undated-known@provenance.test", Earliest);
        var undatedKnown = mailbox.Add(
            MailFolderSpecialUse.Inbox,
            "undated-known@provenance.test",
            receivedAt: null,
            messageId: "undated-known@provenance.test");

        await SeedAsync(services, [mailbox], cancellationToken);

        // Act
        var provenance = await ReadAsync(
            services,
            [mailbox],
            [firstOfTwo, secondOfTwo, copyInInbox, copyInArchive, undated, undatedKnown],
            cancellationToken);

        // Assert
        Assert.True(provenance[firstOfTwo].IsFirstContact);
        Assert.False(provenance[secondOfTwo].IsFirstContact);
        Assert.True(provenance[copyInInbox].IsFirstContact);
        Assert.True(provenance[copyInArchive].IsFirstContact);
        Assert.True(provenance[undated].IsFirstContact);
        Assert.False(provenance[undatedKnown].IsFirstContact);
    }

    /// <summary>
    /// Earlier mail counts only while it is still part of the mailbox and sits in a folder a tool may read, and a copy of
    /// the person's own mail filed in Sent, which carries no arrival instant, is placed by when it was sent.
    /// </summary>
    [Fact]
    public async Task ReadAsync_EarlierMailExpungedHiddenOrOnlySent_CountsOnlyLiveMailInFoldersVisibleToTools()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var mailbox = new SeededMailbox();

        mailbox.Add(folder: null, "expunged@provenance.test", Earliest, expungedAt: Earlier);
        mailbox.Add(folder: null, "hidden@provenance.test", Earliest, inUnmappedFolder: true);
        mailbox.Add(
            MailFolderSpecialUse.Sent,
            "owner@provenance.test",
            receivedAt: null,
            to: ["answered@provenance.test"],
            sentAt: Earliest);
        mailbox.Add(
            MailFolderSpecialUse.Sent,
            "owner@provenance.test",
            receivedAt: null,
            to: ["answered-later@provenance.test"],
            sentAt: Latest.AddHours(1));

        var afterExpunged = mailbox.Add(MailFolderSpecialUse.Inbox, "expunged@provenance.test", Latest);
        var afterHidden = mailbox.Add(MailFolderSpecialUse.Inbox, "hidden@provenance.test", Latest);
        var afterSent = mailbox.Add(MailFolderSpecialUse.Inbox, "answered@provenance.test", Latest);
        var beforeSent = mailbox.Add(MailFolderSpecialUse.Inbox, "answered-later@provenance.test", Latest);

        await SeedAsync(services, [mailbox], cancellationToken);

        // Act
        var provenance = await ReadAsync(
            services,
            [mailbox],
            [afterExpunged, afterHidden, afterSent, beforeSent],
            cancellationToken);

        // Assert
        Assert.True(provenance[afterExpunged].IsFirstContact);
        Assert.True(provenance[afterHidden].IsFirstContact);
        Assert.False(provenance[afterSent].IsFirstContact);
        Assert.True(provenance[beforeSent].IsFirstContact);
    }

    /// <summary>
    /// Each email answers the verdict recorded on its own row and whether its folder is the junk folder, and an email
    /// the store does not hold is absent from the answer rather than answered with a default.
    /// </summary>
    [Fact]
    public async Task ReadAsync_EmailsInJunkAndInbox_AnswersEachRecordedVerdictAndFolderAndLeavesAnUnknownEmailOut()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var mailbox = new SeededMailbox();

        var inJunk = mailbox.Add(
            MailFolderSpecialUse.Junk,
            "trusted@provenance.test",
            Latest,
            authentication: AuthorAuthenticationOutcome.Authenticated,
            trust: SenderTrustLevel.Trusted);
        var inInbox = mailbox.Add(
            MailFolderSpecialUse.Inbox,
            "failed@provenance.test",
            Latest,
            authentication: AuthorAuthenticationOutcome.Failed);
        var neverStored = StoredEmailId.Create(Guid.NewGuid());

        await SeedAsync(services, [mailbox], cancellationToken);

        // Act
        var provenance = await ReadAsync(services, [mailbox], [inJunk, inInbox, neverStored], cancellationToken);

        // Assert
        Assert.Equal(
            new EmailProvenance
            {
                Sender = new SenderVerification
                {
                    AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                    DeploymentTrust = SenderTrustLevel.Trusted,
                },
                IsInJunkFolder = true,
                IsFirstContact = true,
            },
            provenance[inJunk]);
        Assert.Equal(
            new EmailProvenance
            {
                Sender = new SenderVerification
                {
                    AuthorAuthentication = AuthorAuthenticationOutcome.Failed,
                    DeploymentTrust = SenderTrustLevel.Unknown,
                },
                IsInJunkFolder = false,
                IsFirstContact = true,
            },
            provenance[inInbox]);
        Assert.False(provenance.ContainsKey(neverStored));
    }

    private static async Task SeedAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<SeededMailbox> mailboxes,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                foreach (var mailbox in mailboxes)
                {
                    mailbox.AddTo(context);
                }

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static Task<IReadOnlyDictionary<StoredEmailId, EmailProvenance>> ReadAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<SeededMailbox> visibleToTools,
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MailFolderIdentity> folders = [.. visibleToTools.Select(static mailbox => mailbox.MappedFolder)];
        var deploymentFolders = Substitute.For<IDeploymentMailFolders>();
        deploymentFolders.ReadAsync(MailFolderSelection.VisibleToTools, Arg.Any<CancellationToken>()).Returns(folders);

        return services.InScopeAsync(
            (scope, token) => new StoredEmailProvenanceReader(
                    scope.GetRequiredService<MailFathomDbContext>(),
                    deploymentFolders)
                .ReadAsync(emails, token),
            cancellationToken);
    }

    private static string Normalized(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress.NormalizedAddress
            : throw new ArgumentException($"'{address}' is not an address the domain accepts.", nameof(address));

    /// <summary>One mailbox account of a test's own, with a folder per role and the mail written into them.</summary>
    /// <remarks>
    /// The account identifier is fresh per instance and in the form a deployment generates, so two tests never meet over
    /// a folder role, which the schema holds unique among an account's live folders. Its folders carry no alias, which
    /// admits them within an admitted account the way a folder a person made is admitted, apart from one folder whose
    /// alias no mapping names.
    /// </remarks>
    private sealed class SeededMailbox
    {
        private readonly MailboxAccountEntity account = new() { Id = Guid.NewGuid().ToString() };

        private readonly Dictionary<MailFolderSpecialUse, MailFolderEntity> folders = [];

        private readonly List<StoredEmailEntity> emails = [];

        private MailFolderEntity? folderWithoutRole;

        private MailFolderEntity? unmappedFolder;

        private readonly MailFolderBindingEntity binding;

        public SeededMailbox()
        {
            this.binding = new MailFolderBindingEntity
            {
                MailboxAccountId = this.account.Id,
                MailboxAccount = this.account,
                Alias = "provenance",
                RemotePath = "INBOX",
            };
        }

        /// <summary>Gets the folder identity a tool may read, which admits this account and no alias but its own.</summary>
        public MailFolderIdentity MappedFolder => new(
            MailAccountId.Create(this.account.Id),
            MailFolderAlias.Create("provenance-mapped"));

        public StoredEmailId Add(
            MailFolderSpecialUse? folder,
            string? sender,
            DateTimeOffset? receivedAt,
            string[]? to = null,
            string[]? cc = null,
            string? messageId = null,
            AuthorAuthenticationOutcome authentication = AuthorAuthenticationOutcome.NotEstablished,
            SenderTrustLevel trust = SenderTrustLevel.Unknown,
            DateTimeOffset? sentAt = null,
            DateTimeOffset? expungedAt = null,
            bool inUnmappedFolder = false)
        {
            var email = new StoredEmailEntity
            {
                Id = Guid.NewGuid(),
                MailboxAccountId = this.account.Id,
                MailFolderBinding = this.binding,
                Folder = inUnmappedFolder ? this.UnmappedFolder() : this.FolderOf(folder),
                InternetMessageId = messageId,
                ReceivedAt = receivedAt,
                SentAt = sentAt,
                RemoteExpungeObservedAt = expungedAt,
                SenderAddress = sender,
                SenderNormalizedAddress = sender is null ? null : Normalized(sender),
                ToAddresses = [.. (to ?? []).Select(Normalized)],
                CcAddresses = [.. (cc ?? []).Select(Normalized)],
                AuthorAuthenticationOutcome = authentication,
                SenderTrustLevel = trust,
                ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
                StoredAt = Earliest,
            };

            this.emails.Add(email);

            return StoredEmailId.Create(email.Id);
        }

        public void AddTo(MailFathomDbContext context)
        {
            context.MailboxAccounts.Add(this.account);
            context.MailFolderBindings.Add(this.binding);
            context.MailFolders.AddRange(this.folders.Values);

            if (this.folderWithoutRole is not null)
            {
                context.MailFolders.Add(this.folderWithoutRole);
            }

            if (this.unmappedFolder is not null)
            {
                context.MailFolders.Add(this.unmappedFolder);
            }

            context.StoredEmails.AddRange(this.emails);
        }

        private MailFolderEntity UnmappedFolder() => this.unmappedFolder ??= new MailFolderEntity
        {
            Id = Guid.NewGuid(),
            MailboxAccountId = this.account.Id,
            Alias = "provenance-unmapped",
            Name = "Unmapped",
            NameKey = "UNMAPPED",
        };

        private MailFolderEntity FolderOf(MailFolderSpecialUse? role)
        {
            if (role is null)
            {
                return this.folderWithoutRole ??= new MailFolderEntity
                {
                    Id = Guid.NewGuid(),
                    MailboxAccountId = this.account.Id,
                    Name = "Kept",
                    NameKey = "KEPT",
                };
            }

            if (!this.folders.TryGetValue(role.Value, out var folder))
            {
                folder = new MailFolderEntity
                {
                    Id = Guid.NewGuid(),
                    MailboxAccountId = this.account.Id,
                    Name = role.Value.ToString(),
                    NameKey = role.Value.ToString().ToUpperInvariant(),
                    Role = role,
                };
                this.folders.Add(role.Value, folder);
            }

            return folder;
        }
    }
}
