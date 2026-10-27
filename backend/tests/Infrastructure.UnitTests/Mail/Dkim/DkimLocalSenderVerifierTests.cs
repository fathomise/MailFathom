// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Mail;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Mail.Dkim;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

public sealed class DkimLocalSenderVerifierTests
{
    /// <summary>A signature that verifies against the published key names the domain that made it.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureVerifying_AuthenticatesTheSigningDomain()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(signed.PublicKeyRecord), DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, verdict.Outcome);
        Assert.Equal(SenderAuthenticationSource.LocalVerification, verdict.Source);
        Assert.Equal(SenderAuthenticationMethod.DomainKeysIdentifiedMail, verdict.AuthenticatedBy);
        Assert.Equal("SIGNER.EXAMPLE.TEST", verdict.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, verdict.AuthorAuthentication);
    }

    /// <summary>Nothing an envelope carried survives delivery, so a locally reached verdict names neither half of it.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureVerifying_NamesNoSpfIdentityAndNoDmarcResult()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(signed.PublicKeyRecord), DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        Assert.Null(verdict.SpfDomain);
        Assert.Equal(DmarcOutcome.NotReported, verdict.Dmarc);
    }

    /// <summary>A signature made for another domain than the author's establishes the transport and not the author.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureFromAnotherDomain_LeavesTheAuthorUnestablished()
    {
        // Arrange
        var signed = DkimFixtures.Sign(fromAddress: "anna@bank.example.test", signingDomain: "relay.example.test");
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(signed.PublicKeyRecord), DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@bank.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, verdict.Outcome);
        Assert.Equal("RELAY.EXAMPLE.TEST", verdict.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, verdict.AuthorAuthentication);
    }

    /// <summary>Bytes that moved after the signature was made do not verify, which is a failure rather than silence.</summary>
    [Fact]
    public async Task VerifyAsync_BodyChangedAfterSigning_RecordsAFailure()
    {
        // Arrange
        var signed = DkimFixtures.Sign(body: "Dzień dobry.");
        using var message = DkimFixtures.Parse(TamperedBody(signed.RawMime));
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(signed.PublicKeyRecord), DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Failed, verdict.Outcome);
        Assert.Equal(SenderAuthenticationSource.LocalVerification, verdict.Source);
        Assert.Null(verdict.AuthenticatedDomain);
    }

    /// <summary>A key nothing publishes leaves the verdict not established, because nothing was checked.</summary>
    [Fact]
    public async Task VerifyAsync_KeyThatCannotBeResolved_LeavesTheVerdictNotEstablished()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(record: null), DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, verdict.Outcome);
        Assert.Equal(SenderAuthenticationSource.LocalVerification, verdict.Source);
    }

    /// <summary>A record that is published and unreadable is the same fact as one that is absent.</summary>
    [Fact]
    public async Task VerifyAsync_KeyRecordThatCannotBeParsed_LeavesTheVerdictNotEstablished()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering("v=DKIM1; k=rsa; p=not-a-key"), DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, verdict.Outcome);
        Assert.Equal(SenderAuthenticationSource.LocalVerification, verdict.Source);
    }

    /// <summary>A message carrying no signature is verified without a single lookup, and establishes nothing.</summary>
    [Fact]
    public async Task VerifyAsync_MessageWithNoSignature_ResolvesNothing()
    {
        // Arrange
        var resolver = ResolverAnswering(record: null);
        using var message = DkimFixtures.Parse(
            Encoding.UTF8.GetBytes(string.Join("\r\n", "From: anna@example.test", "Subject: Plain", string.Empty, "Body.")));
        var verifier = new DkimLocalSenderVerifier(resolver, DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, verdict.Outcome);
        await resolver.DidNotReceive().ResolveAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The verification asks the selector and domain the signature named, and nothing derived from the author.</summary>
    [Fact]
    public async Task VerifyAsync_SignedMessage_ResolvesTheSelectorAndDomainTheSignatureNamed()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var resolver = ResolverAnswering(signed.PublicKeyRecord);

        // Act
        await new DkimLocalSenderVerifier(resolver, DmarcRecords(DmarcRecordLookup.NotPublished), new FakeTimeProvider()).VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        await resolver.Received(1).ResolveAsync(
            DkimFixtures.Selector,
            DkimFixtures.SigningDomain,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A lookup that never answers costs the message its budget and then leaves the signature unchecked.</summary>
    /// <remarks>
    /// The number of lookups a message asks for is written by whoever sent it, so the budget over the whole message is
    /// what keeps a nameserver that accepts a query and never answers from holding a folder run open. Reaching it is
    /// not a statement about the mail: the verdict is the not-established one an unresolvable key already produces.
    /// </remarks>
    [Fact]
    public async Task VerifyAsync_LookupThatNeverAnswers_EndsAtTheMessageBudgetAndEstablishesNothing()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var resolver = Substitute.For<IDkimPublicKeyRecordResolver>();
        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => NeverAnswersAsync(call.Arg<CancellationToken>()));

        var signed = DkimFixtures.Sign();
        var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(resolver, DmarcRecords(DmarcRecordLookup.NotPublished), timeProvider);

        try
        {
            // Act
            // The message is disposed in the finally rather than by a using declaration, because the verification has
            // to be in flight while the clock moves and only then awaited.
            var verification = verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);
            timeProvider.Advance(TimeSpan.FromSeconds(30));
            var verdict = await verification;

            // Assert
            Assert.Equal(SenderAuthenticationOutcome.NotEstablished, verdict.Outcome);
            Assert.Equal(SenderAuthenticationSource.LocalVerification, verdict.Source);
        }
        finally
        {
            message.Dispose();
        }
    }

    /// <summary>A signature from beneath the displayed domain aligns by the relation where the domain publishes no record.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureFromBeneathTheDisplayedDomainWithNoRecord_RecordsAlignedDkim()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(
            ResolverAnswering(signed.PublicKeyRecord),
            DmarcRecords(DmarcRecordLookup.NotPublished),
            new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@example.test", CancellationToken.None);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, verdict.AuthorAuthentication);
        Assert.Equal(AuthorAuthenticationRoute.AlignedDkim, verdict.AuthorAuthenticationRoute);
        Assert.Equal(DmarcOutcome.NotReported, verdict.Dmarc);
    }

    /// <summary>A domain publishing <c>adkim=s</c> refuses a signature its subdomain made, and that is no failure.</summary>
    [Fact]
    public async Task VerifyAsync_StrictRecordAndASignatureFromBeneath_LeavesTheAuthorNotEstablished()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(
            ResolverAnswering(signed.PublicKeyRecord),
            DmarcRecords(DmarcRecordLookup.Found(new DmarcRecord(DkimAlignmentMode.Strict))),
            new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@example.test", CancellationToken.None);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, verdict.Outcome);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, verdict.AuthorAuthentication);
        Assert.Equal(AuthorAuthenticationRoute.None, verdict.AuthorAuthenticationRoute);
    }

    /// <summary>A record that could not be read aligns nothing, and says nothing against the sender.</summary>
    [Fact]
    public async Task VerifyAsync_RecordThatCannotBeRead_LeavesTheAuthorNotEstablishedRatherThanFailed()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var verifier = new DkimLocalSenderVerifier(
            ResolverAnswering(signed.PublicKeyRecord),
            DmarcRecords(DmarcRecordLookup.NotReadable),
            new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@signer.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, verdict.AuthorAuthentication);
        Assert.Equal(DmarcOutcome.NotReported, verdict.Dmarc);
    }

    /// <summary>The record asked for is the displayed domain's, and only where a verified signature could align with it.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureThatCouldAlign_AsksForTheDisplayedDomainsRecord()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var dmarcRecords = DmarcRecords(DmarcRecordLookup.NotPublished);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(signed.PublicKeyRecord), dmarcRecords, new FakeTimeProvider());

        // Act
        await verifier.VerifyAsync(message, "anna@example.test", CancellationToken.None);

        // Assert
        await dmarcRecords.Received(1).ResolveAsync(DomainOf("example.test"), Arg.Any<CancellationToken>());
    }

    /// <summary>A signature on another branch could not align under any record, so no record is asked for.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureFromASiblingDomain_AsksForNoRecord()
    {
        // Arrange
        var signed = DkimFixtures.Sign();
        using var message = DkimFixtures.Parse(signed.RawMime);
        var dmarcRecords = DmarcRecords(DmarcRecordLookup.NotPublished);
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(signed.PublicKeyRecord), dmarcRecords, new FakeTimeProvider());

        // Act
        var verdict = await verifier.VerifyAsync(message, "anna@bank.example.test", CancellationToken.None);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, verdict.AuthorAuthentication);
        await dmarcRecords.DidNotReceive().ResolveAsync(Arg.Any<SenderDomain>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A record walk that never answers ends at the message budget and reads as a record that could not be read.</summary>
    [Fact]
    public async Task ReadDmarcRecordAsync_WalkThatNeverAnswers_EndsAtTheMessageBudgetAsNotReadable()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var dmarcRecords = Substitute.For<IDmarcRecordResolver>();
        dmarcRecords
            .ResolveAsync(Arg.Any<SenderDomain>(), Arg.Any<CancellationToken>())
            .Returns(call => NeverFindsARecordAsync(call.Arg<CancellationToken>()));
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(record: null), dmarcRecords, timeProvider);

        // Act
        var reading = verifier.ReadDmarcRecordAsync(DomainOf("example.test"), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var answer = await reading;

        // Assert
        Assert.Same(DmarcRecordLookup.NotReadable, answer);
    }

    /// <summary>The caller giving up is not a record that could not be read, so it propagates.</summary>
    [Fact]
    public async Task ReadDmarcRecordAsync_CallerCancels_Propagates()
    {
        // Arrange
        var dmarcRecords = Substitute.For<IDmarcRecordResolver>();
        dmarcRecords
            .ResolveAsync(Arg.Any<SenderDomain>(), Arg.Any<CancellationToken>())
            .Returns(call => NeverFindsARecordAsync(call.Arg<CancellationToken>()));
        var verifier = new DkimLocalSenderVerifier(ResolverAnswering(record: null), dmarcRecords, new FakeTimeProvider());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var reading = verifier.ReadDmarcRecordAsync(DomainOf("example.test"), cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    private static async Task<DmarcRecordLookup> NeverFindsARecordAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return DmarcRecordLookup.NotPublished;
    }

    /// <summary>Answers every displayed domain with one record lookup.</summary>
    private static IDmarcRecordResolver DmarcRecords(DmarcRecordLookup answer)
    {
        var resolver = Substitute.For<IDmarcRecordResolver>();
        resolver.ResolveAsync(Arg.Any<SenderDomain>(), Arg.Any<CancellationToken>()).Returns(answer);

        return resolver;
    }

    private static SenderDomain DomainOf(string written)
    {
        SenderDomain.TryCreate(written, out var domain);

        return domain;
    }

    /// <summary>Stands in for a nameserver that accepts the query and answers only when the caller gives up.</summary>
    private static async Task<string?> NeverAnswersAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return null;
    }

    /// <summary>Answers every selector with one record, or with the absence of one.</summary>
    private static IDkimPublicKeyRecordResolver ResolverAnswering(string? record)
    {
        var resolver = Substitute.For<IDkimPublicKeyRecordResolver>();
        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(record);

        return resolver;
    }

    /// <summary>Changes one word of the body, leaving every signed header exactly as it was.</summary>
    private static byte[] TamperedBody(byte[] rawMime) =>
        Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(rawMime).Replace("Dzień dobry.", "Dzień dobry!", StringComparison.Ordinal));
}
