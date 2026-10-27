// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using DnsClient;
using DnsClient.Protocol;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Mail.Dkim;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

public sealed class DnsDmarcRecordResolverTests
{
    /// <summary>The <c>NXDOMAIN</c> response code, which is the low four bits of a response header's flags.</summary>
    private const ushort NotExistentDomainFlags = 3;

    /// <summary>The <c>SERVFAIL</c> response code.</summary>
    private const ushort ServerFailureFlags = 2;

    /// <summary>A record at the displayed domain is the record, and no parent is asked.</summary>
    [Fact]
    public async Task ResolveAsync_RecordAtTheDisplayedDomain_FindsItAndAsksNoParent()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.bank.example.test"] = Records("v=DMARC1; p=reject; adkim=s"),
        });

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(DkimAlignmentMode.Strict, answer.Record?.DkimAlignment);
        Assert.Equal(["_dmarc.bank.example.test"], QueriedNames(lookup));
    }

    /// <summary>A name with no record, or with no answer at all, is passed over for its parent.</summary>
    [Fact]
    public async Task ResolveAsync_NoRecordBelow_WalksUpToTheFirstParentHoldingOne()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.mail.bank.example.test"] = NotExistentDomain(),
            ["_dmarc.bank.example.test"] = Records("v=spf1 -all"),
            ["_dmarc.example.test"] = Records("v=DMARC1; p=none"),
        });

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("mail.bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(DkimAlignmentMode.Relaxed, answer.Record?.DkimAlignment);
        Assert.Equal(
            ["_dmarc.mail.bank.example.test", "_dmarc.bank.example.test", "_dmarc.example.test"],
            QueriedNames(lookup));
    }

    /// <summary>Where nothing publishes a record the walk asks down to and including the single-label name.</summary>
    [Fact]
    public async Task ResolveAsync_NothingPublishedAnywhere_AsksEveryNameAndFindsNone()
    {
        // Arrange
        var lookup = LookupAnswering(new());

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Same(DmarcRecordLookup.NotPublished, answer);
        Assert.Equal(["_dmarc.bank.example.test", "_dmarc.example.test", "_dmarc.test"], QueriedNames(lookup));
    }

    /// <summary>A long name costs at most eight queries, because its parents are walked from seven labels.</summary>
    [Fact]
    public async Task ResolveAsync_NameOfTenLabels_AsksAtMostEightNames()
    {
        // Arrange
        var lookup = LookupAnswering(new());

        // Act
        await CreateResolver(lookup).ResolveAsync(DomainOf("a.b.c.d.e.f.g.h.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(
            [
                "_dmarc.a.b.c.d.e.f.g.h.example.test",
                "_dmarc.d.e.f.g.h.example.test",
                "_dmarc.e.f.g.h.example.test",
                "_dmarc.f.g.h.example.test",
                "_dmarc.g.h.example.test",
                "_dmarc.h.example.test",
                "_dmarc.example.test",
                "_dmarc.test",
            ],
            QueriedNames(lookup));
    }

    /// <summary>Two records at one name are discarded together, and the walk goes on as though there were none.</summary>
    [Fact]
    public async Task ResolveAsync_TwoRecordsAtOneName_ReadsAsNoRecordThereAndKeepsWalking()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.bank.example.test"] = Records("v=DMARC1; p=none", "v=DMARC1; p=reject; adkim=s"),
            ["_dmarc.example.test"] = Records("v=DMARC1; p=none; adkim=r"),
        });

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(DkimAlignmentMode.Relaxed, answer.Record?.DkimAlignment);
    }

    /// <summary>A record published as several strings is read joined, so a tag in a later string still counts.</summary>
    [Fact]
    public async Task ResolveAsync_RecordInSeveralStrings_ReadsTheTagsOfEveryString()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.bank.example.test"] = RecordInParts("v=DMARC1; p=reject; rua=mailto:reports@example.test", "; adkim=s"),
        });

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(DkimAlignmentMode.Strict, answer.Record?.DkimAlignment);
    }

    /// <summary>A record past the size bound is never parsed, so its name reads as holding none and the walk goes on.</summary>
    [Fact]
    public async Task ResolveAsync_RecordPastTheSizeBound_ReadsAsNoRecordThereAndKeepsWalking()
    {
        // Arrange
        var strictRecord = "v=DMARC1; p=reject; adkim=s; rua=";
        var oversized = strictRecord + new string('x', DnsDkimPublicKeyRecordResolver.MaximumRecordLength - strictRecord.Length + 1);
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.bank.example.test"] = Records(oversized),
            ["_dmarc.example.test"] = Records("v=DMARC1; p=none; adkim=r"),
        });

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(DkimAlignmentMode.Relaxed, answer.Record?.DkimAlignment);
        Assert.Equal(["_dmarc.bank.example.test", "_dmarc.example.test"], QueriedNames(lookup));
    }

    /// <summary>A server failure ends the walk unread, since a parent's record might not be the one that governs.</summary>
    [Fact]
    public async Task ResolveAsync_ServerFailure_EndsTheWalkAsNotReadable()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.bank.example.test"] = Failed(ServerFailureFlags),
            ["_dmarc.example.test"] = Records("v=DMARC1; p=none"),
        });

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Same(DmarcRecordLookup.NotReadable, answer);
        Assert.Equal(["_dmarc.bank.example.test"], QueriedNames(lookup));
    }

    /// <summary>Every configured nameserver failing is a name that could not be read.</summary>
    [Fact]
    public async Task ResolveAsync_EveryNameserverFails_IsNotReadable()
    {
        // Arrange
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns<IDnsQueryResponse>(_ => throw new DnsResponseException(DnsResponseCode.ConnectionTimeout));

        // Act
        var answer = await CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Same(DmarcRecordLookup.NotReadable, answer);
    }

    /// <summary>A lookup that outlives its deadline is a name that could not be read, not a domain without a record.</summary>
    [Fact]
    public async Task ResolveAsync_LookupOutlivesItsDeadline_IsNotReadable()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForeverAsync(call.Arg<CancellationToken>()));
        var resolver = new DnsDmarcRecordResolver(lookup, new DnsRecordCache(timeProvider), timeProvider, TimeSpan.FromSeconds(1));

        // Act
        var resolving = resolver.ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var answer = await resolving;

        // Assert
        Assert.Same(DmarcRecordLookup.NotReadable, answer);
    }

    /// <summary>A domain asked for twice is answered from the cache the second time, record and absences alike.</summary>
    [Fact]
    public async Task ResolveAsync_TheSameDomainTwice_QueriesEachNameOnce()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.example.test"] = Records("v=DMARC1; p=reject; adkim=s"),
        });
        var resolver = CreateResolver(lookup);

        // Act
        await resolver.ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);
        var second = await resolver.ResolveAsync(DomainOf("bank.example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(DkimAlignmentMode.Strict, second.Record?.DkimAlignment);
        Assert.Equal(["_dmarc.bank.example.test", "_dmarc.example.test"], QueriedNames(lookup));
    }

    /// <summary>An error is not held, so a nameserver that recovers is asked again on the next message.</summary>
    [Fact]
    public async Task ResolveAsync_AfterAServerFailure_AsksTheNameAgain()
    {
        // Arrange
        var lookup = LookupAnswering(new Dictionary<string, IDnsQueryResponse>
        {
            ["_dmarc.example.test"] = Failed(ServerFailureFlags),
        });
        var resolver = CreateResolver(lookup);

        // Act
        await resolver.ResolveAsync(DomainOf("example.test"), CancellationToken.None);
        await resolver.ResolveAsync(DomainOf("example.test"), CancellationToken.None);

        // Assert
        Assert.Equal(["_dmarc.example.test", "_dmarc.example.test"], QueriedNames(lookup));
    }

    /// <summary>The caller giving up is not a name that could not be read, so it propagates.</summary>
    [Fact]
    public async Task ResolveAsync_CallerCancels_Propagates()
    {
        // Arrange
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForeverAsync(call.Arg<CancellationToken>()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var resolving = CreateResolver(lookup).ResolveAsync(DomainOf("bank.example.test"), cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
    }

    private static DnsDmarcRecordResolver CreateResolver(IDnsQuery lookup)
    {
        var timeProvider = new FakeTimeProvider();

        return new DnsDmarcRecordResolver(lookup, new DnsRecordCache(timeProvider), timeProvider);
    }

    /// <summary>Answers each name with the response a test gave it, and every other name with an empty answer.</summary>
    private static IDnsQuery LookupAnswering(Dictionary<string, IDnsQueryResponse> responsesByName)
    {
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(call => responsesByName.TryGetValue(call.Arg<string>(), out var response) ? response : Records());

        return lookup;
    }

    private static string[] QueriedNames(IDnsQuery lookup) =>
        [.. lookup.ReceivedCalls().Select(static call => (string)call.GetArguments()[0]!)];

    private static IDnsQueryResponse Records(params string[] texts) =>
        Answering([.. texts.Select(static text => TxtRecordOf([text]))]);

    private static IDnsQueryResponse RecordInParts(params string[] parts) => Answering([TxtRecordOf(parts)]);

    private static IDnsQueryResponse Answering(TxtRecord[] records)
    {
        var response = Substitute.For<IDnsQueryResponse>();
        response.HasError.Returns(false);
        response.Header.Returns(new DnsResponseHeader(0, 0, 1, records.Length, 0, 0));
        response.Answers.Returns([.. records]);

        return response;
    }

    private static IDnsQueryResponse NotExistentDomain() => Failed(NotExistentDomainFlags);

    private static IDnsQueryResponse Failed(ushort flags)
    {
        var response = Substitute.For<IDnsQueryResponse>();
        response.HasError.Returns(true);
        response.Header.Returns(new DnsResponseHeader(0, flags, 1, 0, 0, 0));
        response.Answers.Returns([]);

        return response;
    }

    private static TxtRecord TxtRecordOf(string[] parts) => new(
        new ResourceRecordInfo("_dmarc.example.test", ResourceRecordType.TXT, QueryClass.IN, timeToLive: 3600, rawDataLength: 0),
        parts,
        parts);

    private static SenderDomain DomainOf(string written)
    {
        SenderDomain.TryCreate(written, out var domain);

        return domain;
    }

    private static async Task<IDnsQueryResponse> WaitForeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The wait never completes.");
    }
}
