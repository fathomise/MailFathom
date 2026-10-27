// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class DmarcRecordLookupTests
{
    private static readonly DmarcRecordLookup Strict = DmarcRecordLookup.Found(new DmarcRecord(DkimAlignmentMode.Strict));
    private static readonly DmarcRecordLookup Relaxed = DmarcRecordLookup.Found(new DmarcRecord(DkimAlignmentMode.Relaxed));

    public static TheoryData<string, string, string, bool> Alignments => new()
    {
        // Relaxed, and no record at all, keep the relation: equal, beneath, or above, at a label boundary.
        { "relaxed", "bank.test", "bank.test", true },
        { "relaxed", "mail.bank.test", "bank.test", true },
        { "relaxed", "bank.test", "news.bank.test", true },
        { "relaxed", "shop.bank.test", "news.bank.test", false },
        { "none", "mail.bank.test", "bank.test", true },
        { "none", "notbank.test", "bank.test", false },

        // Strict keeps equality alone.
        { "strict", "bank.test", "bank.test", true },
        { "strict", "mail.bank.test", "bank.test", false },
        { "strict", "bank.test", "news.bank.test", false },

        // A record that could not be read aligns nothing, the exact domain included.
        { "unreadable", "bank.test", "bank.test", false },
    };

    /// <summary>The domain's record narrows the relation a DKIM signature aligns by, and never widens it.</summary>
    [Theory]
    [MemberData(nameof(Alignments))]
    public void AlignsDkim_UnderEachAnswer_FollowsTheRecord(string answer, string signing, string from, bool expected)
    {
        // Arrange
        var lookup = answer switch
        {
            "strict" => Strict,
            "relaxed" => Relaxed,
            "none" => DmarcRecordLookup.NotPublished,
            _ => DmarcRecordLookup.NotReadable,
        };

        // Act
        var aligned = lookup.AlignsDkim(DomainOf(signing), DomainOf(from));

        // Assert
        Assert.Equal(expected, aligned);
    }

    private static SenderDomain DomainOf(string written)
    {
        SenderDomain.TryCreate(written, out var domain);

        return domain;
    }
}
