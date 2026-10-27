// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Preferences;
using Xunit;

namespace MailFathom.Application.UnitTests.Preferences;

/// <summary>
/// Covers what a person's list of trusted link domains will hold. What it has to hold is that an entry is kept as the name
/// a browser compares, and that nothing is kept which would let a link to somebody else's site open unasked.
/// </summary>
public sealed class TrustedLinkDomainTests
{
    [Theory]
    [InlineData("nordwind.example", "nordwind.example")]
    [InlineData("Files.Nordwind.Example", "files.nordwind.example")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("nordwind.co.uk", "nordwind.co.uk")]
    [InlineData("nordwind.uk", "nordwind.uk")]
    public void TryRead_ADomainName_KeepsItAsTheNameABrowserCompares(string written, string kept)
    {
        // Act
        var read = TrustedLinkDomain.TryRead(written, out var domain, out var refusal);

        // Assert
        Assert.True(read, refusal);
        Assert.Equal(kept, domain!.Name);
    }

    [Theory]
    [InlineData("", "is empty.")]
    [InlineData("https://nordwind.example", "carries a scheme")]
    [InlineData("nordwind.example/billing", "carries a scheme")]
    [InlineData("nordwind.example:8443", "carries a scheme")]
    [InlineData("*.nordwind.example", "carries a wildcard")]
    [InlineData("192.0.2.10", "is an address")]
    [InlineData("example", "is a public suffix")]
    [InlineData("co.uk", "is a public suffix")]
    [InlineData("com.pl", "is a public suffix")]
    [InlineData("nordwind..example", "is not a domain name")]
    [InlineData("nordwind example", "is not a domain name")]
    [InlineData("nordwind_billing.example", "is not a domain name")]
    public void TryRead_AnEntryThatIsNoBareDomain_RefusesSayingWhy(string written, string reason)
    {
        // Act
        var read = TrustedLinkDomain.TryRead(written, out var domain, out var refusal);

        // Assert
        Assert.False(read);
        Assert.Null(domain);
        Assert.StartsWith(reason, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_AnEntryLongerThanADomainNameCanBe_IsRefused()
    {
        // Arrange
        var written = string.Join('.', Enumerable.Repeat(new string('a', 60), 5)) + ".example";

        // Act
        var read = TrustedLinkDomain.TryRead(written, out _, out var refusal);

        // Assert
        Assert.False(read);
        Assert.StartsWith("is longer than", refusal, StringComparison.Ordinal);
    }
}
