// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReadSource;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.Host.Security.Endpoints;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers how the client's source route turns a read of the local copy into a response, and every refusal it answers.</summary>
/// <remarks>
/// The octets are sender-controlled and served from the client's own origin, so what is asserted beside them is that
/// they arrive as text nothing will render, that nothing keeps a copy, and that a cut source says how large the whole is.
/// </remarks>
public sealed class ClientMailSourceEndpointTests
{
    private static readonly Guid Message = new("55555555-5555-5555-5555-555555555555");

    private static readonly byte[] StoredRawMime =
        "From: sender@example.test\r\nSubject: <script>alert(1)</script>\r\n\r\nBody"u8.ToArray();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailSourceRoute_IsThePathAClientComposes() =>
        Assert.Equal("/messages/{storedEmailId:guid}/source", ClientMailSourceEndpoint.MailSourceRoute);

    [Fact]
    public async Task ReadSourceAsync_MessageOfThisReader_WritesTheOctetsAsTextNothingRendersOrKeeps()
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        var result = await ClientMailSourceEndpoint.ReadSourceAsync(
            Message,
            SourceReading(StoredRawMime),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<EmptyHttpResult>(result.Result);
        Assert.Equal(StoredRawMime, body.ToArray());
        Assert.Equal("text/plain; charset=utf-8", context.Response.ContentType);
        Assert.Equal(StoredRawMime.Length, context.Response.ContentLength);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.False(context.Response.Headers.ContainsKey(ClientMailSourceEndpoint.TruncatedFromHeaderName));
    }

    [Fact]
    public async Task ReadSourceAsync_SourceLargerThanTheBound_WritesTheBoundAndStatesTheWholeSize()
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var largeMime = new byte[EmailSourceReader.MaximumServedOctets + 10];

        // Act
        await ClientMailSourceEndpoint.ReadSourceAsync(
            Message,
            SourceReading(largeMime),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSourceReader.MaximumServedOctets, body.Length);
        Assert.Equal(
            largeMime.Length.ToString(CultureInfo.InvariantCulture),
            context.Response.Headers[ClientMailSourceEndpoint.TruncatedFromHeaderName].ToString());
    }

    /// <summary>A message this reader holds no source for answers as one that does not exist, whatever the reason.</summary>
    [Fact]
    public async Task ReadSourceAsync_MessageWithNoLocalCopy_RefusesAsNotFound()
    {
        // Act
        var result = await ClientMailSourceEndpoint.ReadSourceAsync(
            Message,
            SourceReading(storedRawMime: null),
            new DefaultHttpContext(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task ReadSourceAsync_EmptyIdentifier_RefusesWithoutReadingAnything()
    {
        // Arrange
        var contentStore = Substitute.For<IEmailContentStore>();

        // Act
        var result = await ClientMailSourceEndpoint.ReadSourceAsync(
            Guid.Empty,
            SourceReading(StoredRawMime, contentStore),
            new DefaultHttpContext(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadSourceAsync_AccountWhoseMailIsScreened_RefusesWithItsOwnCode()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding("sk-live-000111222333", TimeProvider.System);

        // Act
        var result = await ClientMailSourceEndpoint.ReadSourceAsync(
            Message,
            SourceReading(StoredRawMime, egressGuard: egress.Guard),
            new DefaultHttpContext(),
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        Assert.Equal(
            MailFathomErrorCode.EmailSourceScreened.Value,
            refused.ProblemDetails.Extensions[RouteAuthorization.ErrorCodeExtension]);
    }

    /// <summary>Builds the real use case over ports that answer with the stored copy a test wants served, or with none.</summary>
    private static EmailSourceReader SourceReading(
        byte[]? storedRawMime,
        IEmailContentStore? contentStore = null,
        SensitiveContentEgressGuard? egressGuard = null)
    {
        var summary = SummaryOf();

        var summaryReader = Substitute.For<IStoredEmailSummaryReader>();
        summaryReader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmailSummary?>(summary));

        var resolvedContentStore = contentStore ?? Substitute.For<IEmailContentStore>();
        resolvedContentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedRawMime is null
                ? null
                : new StoredEmailContent(storedRawMime, storedRawMime.LongLength, SHA256.HashData(storedRawMime))));

        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(summary.Account)]);

        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.CallerActingFor(SyntheticUser.Deployment, "client-key", [MailFathomPermission.MailRead]));

        var folders = StubMailFolderParticipation.Mapping(new MailFolderIdentity(summary.Account, summary.Folder.Alias!.Value));

        return new EmailSourceReader(
            summaryReader,
            resolvedContentStore,
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                accountCatalog,
                folders,
                StubDeploymentMailFolders.Of(folders),
                StubMailFolderMappings.ResolvingNothing),
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            new AccessAuthorization(principals));
    }

    private static EmailSummary SummaryOf() => new()
    {
        StoredEmailId = StoredEmailId.Create(Message),
        Account = MailAccountId.Create("primary"),
        Folder = new StoredEmailFolder(MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3")), "INBOX", MailFolderAlias.Create("INBOX")),
        InternetMessageId = "<abc@example.test>",
        Subject = "Quarterly figures",
        SenderAddress = "billing@example.test",
        ToAddresses = ["reader@example.test"],
        SizeOctets = 1024,
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        ContentAvailability = StoredEmailContentAvailability.Available,
        Attachments = new StoredEmailAttachmentSummary(
            AttachmentCount: 0,
            TotalSizeOctets: 0,
            InlineResourceCount: 0,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
    };
}
