// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class KnownAuthenticationProviderTests
{
    /// <summary>A host is matched without regard to case and without a trailing dot, so every spelling reaches one entry.</summary>
    /// <param name="written">The host as an account or an operator wrote it.</param>
    [Theory]
    [InlineData("imap.gmail.com")]
    [InlineData("IMAP.Gmail.COM")]
    [InlineData("imap.gmail.com.")]
    [InlineData("  imap.gmail.com  ")]
    public void TryNormalizeImapHost_HostWrittenDifferently_ReachesOneMatchingForm(string written)
    {
        // Act
        var host = KnownAuthenticationProvider.TryNormalizeImapHost(written);

        // Assert
        Assert.Equal("imap.gmail.com", host);
    }

    /// <summary>A port, a wildcard, a path, or anything the authority rules refuse is not a host an entry can be matched by.</summary>
    /// <param name="written">The rejected value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("imap.gmail.com:993")]
    [InlineData("*.gmail.com")]
    [InlineData("imap.gmail.com/inbox")]
    [InlineData("imap gmail com")]
    public void TryNormalizeImapHost_ValueNoHostMatches_IsRefused(string? written)
    {
        // Act
        var host = KnownAuthenticationProvider.TryNormalizeImapHost(written);

        // Assert
        Assert.Null(host);
    }

    /// <summary>An address is a host an account may connect to, so it can be listed like a name.</summary>
    [Fact]
    public void TryNormalizeImapHost_Address_IsAccepted()
    {
        // Act
        var host = KnownAuthenticationProvider.TryNormalizeImapHost("192.0.2.10");

        // Assert
        Assert.Equal("192.0.2.10", host);
    }

    /// <summary>An authserv-id is refused by exactly the rules an account's own configured one is.</summary>
    /// <param name="written">The rejected value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mx google com")]
    public void TryNormalizeAuthorityIdentifier_ValueTheAuthorityRulesRefuse_IsRefused(string? written)
    {
        // Act
        var identifier = KnownAuthenticationProvider.TryNormalizeAuthorityIdentifier(written);

        // Assert
        Assert.Null(identifier);
    }

    /// <summary>An entry believes headers its listed server stamped, in any casing, and no other server's.</summary>
    [Fact]
    public void Authority_ListedEntry_BelievesOnlyTheListedServer()
    {
        // Arrange
        var provider = new KnownAuthenticationProvider("IMAP.gmail.com", " MX.Google.com ");

        // Act
        var authority = provider.Authority;

        // Assert
        Assert.Equal("imap.gmail.com", provider.ImapHost);
        Assert.Equal("mx.google.com", provider.AuthorityIdentifier);
        Assert.True(authority.Produced("mx.google.com"));
        Assert.False(authority.Produced("attacker.test"));
    }

    /// <summary>Constructing an entry from a malformed half fails rather than storing something no lookup could reach.</summary>
    [Fact]
    public void Constructor_MalformedHost_Throws()
    {
        // Act
        var thrown = Record.Exception(() => new KnownAuthenticationProvider("*.gmail.com", "mx.google.com"));

        // Assert
        Assert.IsType<ArgumentException>(thrown);
    }
}
