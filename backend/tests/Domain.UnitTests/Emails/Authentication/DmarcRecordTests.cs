// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class DmarcRecordTests
{
    /// <summary>The alignment mode is the <c>adkim</c> tag, relaxed wherever the record does not ask for strict.</summary>
    [Theory]
    [InlineData("v=DMARC1; p=reject; adkim=s", DkimAlignmentMode.Strict)]
    [InlineData("v=DMARC1;p=none;ADKIM = S", DkimAlignmentMode.Strict)]
    [InlineData("v=DMARC1; p=none; adkim=r", DkimAlignmentMode.Relaxed)]
    [InlineData("v=DMARC1; p=quarantine", DkimAlignmentMode.Relaxed)]
    [InlineData("v=DMARC1; adkim=x", DkimAlignmentMode.Relaxed)]
    [InlineData("v = DMARC1", DkimAlignmentMode.Relaxed)]
    public void TryParse_ARecord_ReadsTheAlignmentMode(string text, DkimAlignmentMode expected)
    {
        // Act
        var parsed = DmarcRecord.TryParse(text, out var record);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expected, record?.DkimAlignment);
    }

    /// <summary>Only a string that begins with the version tag is a record, whatever else it carries.</summary>
    [Theory]
    [InlineData("v=spf1 -all")]
    [InlineData("p=reject; v=DMARC1; adkim=s")]
    [InlineData("v=dmarc1; adkim=s")]
    [InlineData("hello; v=DMARC1")]
    [InlineData("")]
    public void TryParse_NotADmarcRecord_IsRefused(string text)
    {
        // Act
        var parsed = DmarcRecord.TryParse(text, out var record);

        // Assert
        Assert.False(parsed);
        Assert.Null(record);
    }

    /// <summary>The walk asks the displayed domain first and then each parent, down to the single-label name.</summary>
    [Fact]
    public void DiscoveryNames_ShortDomain_ListsItAndEveryParent()
    {
        // Act
        var names = DmarcRecord.DiscoveryNames(DomainOf("mail.bank.example.test"));

        // Assert
        Assert.Equal(
            ["MAIL.BANK.EXAMPLE.TEST", "BANK.EXAMPLE.TEST", "EXAMPLE.TEST", "TEST"],
            names.Select(static name => name.NormalizedValue));
    }

    /// <summary>A name of eight or more labels is walked from seven, so no domain costs more than eight lookups.</summary>
    [Theory]
    [InlineData("a.b.c.d.e.f.g.test", 8)]
    [InlineData("a.b.c.d.e.f.g.h.i.j.k.test", 8)]
    [InlineData("test", 1)]
    public void DiscoveryNames_AnyLength_AsksAtMostEightNames(string domain, int expectedCount)
    {
        // Act
        var names = DmarcRecord.DiscoveryNames(DomainOf(domain));

        // Assert
        Assert.Equal(expectedCount, names.Count);
        Assert.Equal(DomainOf(domain), names[0]);
        Assert.Equal(DomainOf("test"), names[^1]);
    }

    /// <summary>The first parent of a long name keeps its last seven labels, which is RFC 9989's shortening.</summary>
    [Fact]
    public void DiscoveryNames_LongDomain_ShortensTheFirstParentToSevenLabels()
    {
        // Act
        var names = DmarcRecord.DiscoveryNames(DomainOf("a.b.c.d.e.f.g.h.i.test"));

        // Assert
        Assert.Equal("D.E.F.G.H.I.TEST", names[1].NormalizedValue);
    }

    private static SenderDomain DomainOf(string written)
    {
        SenderDomain.TryCreate(written, out var domain);

        return domain;
    }
}
