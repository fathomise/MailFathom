// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>One provider whose receiving server every account reading mail from its IMAP host believes by default.</summary>
/// <remarks>
/// <para>
/// An entry is a security decision rather than a convenience: a provider that lets an incoming
/// <c>Authentication-Results</c> header carrying its own authserv-id through would let anybody forge a passing verdict
/// under it. What makes one safe is verified before it is added, which nothing here can check; what is checked here is
/// that both halves are values a match can be made against.
/// </para>
/// <para>
/// The host is matched exactly, without regard to case and without a trailing dot, and never by wildcard, suffix, or
/// port, so a mailbox reached through a gateway or a proxy has no entry and is configured by hand. Both halves are kept
/// lower-cased, which is the form an operator reads in a listing and the form a lookup compares.
/// </para>
/// </remarks>
public sealed record KnownAuthenticationProvider
{
    /// <summary>Initializes an entry, refusing either half where it cannot be matched.</summary>
    /// <param name="imapHost">The IMAP host an account synchronizes from.</param>
    /// <param name="authorityIdentifier">The authserv-id that provider's receiving server writes.</param>
    /// <exception cref="ArgumentException">Thrown when either value is not one <see cref="TryNormalizeImapHost" /> or <see cref="TryNormalizeAuthorityIdentifier" /> accepts.</exception>
    public KnownAuthenticationProvider(string imapHost, string authorityIdentifier)
    {
        this.ImapHost = TryNormalizeImapHost(imapHost)
            ?? throw new ArgumentException("The IMAP host is not a host name or address an entry can be matched by.", nameof(imapHost));
        this.AuthorityIdentifier = TryNormalizeAuthorityIdentifier(authorityIdentifier)
            ?? throw new ArgumentException("The authserv-id is not one a header can be matched against.", nameof(authorityIdentifier));
    }

    /// <summary>Gets the IMAP host an account synchronizes from, in its matching form.</summary>
    public string ImapHost { get; }

    /// <summary>Gets the authserv-id that provider's receiving server writes, lower-cased.</summary>
    public string AuthorityIdentifier { get; }

    /// <summary>Gets the authority an account on this host believes.</summary>
    public TrustedAuthenticationAuthority Authority
    {
        get
        {
            TrustedAuthenticationAuthority.TryCreate(this.AuthorityIdentifier, out var authority);

            return authority;
        }
    }

    /// <summary>Puts a host in the form an entry is matched by, or reports that it cannot be matched at all.</summary>
    /// <param name="imapHost">The host an account connects to, or one an operator wrote.</param>
    /// <returns>The lower-cased host without a trailing dot, or <see langword="null" /> when it is not a host name or address.</returns>
    /// <remarks>
    /// The rules <see cref="TrustedAuthenticationAuthority" /> applies hold here as well, and the host has to read as a
    /// DNS name or an address besides, which is what refuses a port, a wildcard, and a path.
    /// </remarks>
    public static string? TryNormalizeImapHost(string? imapHost)
    {
        if (TryNormalizeAuthorityIdentifier(imapHost) is not { } candidate)
        {
            return null;
        }

        var host = candidate.TrimEnd('.');

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? host
            : null;
    }

    /// <summary>Puts an authserv-id in the form an entry holds, or reports that it is unusable.</summary>
    /// <param name="authorityIdentifier">The authserv-id an operator wrote.</param>
    /// <returns>The trimmed, lower-cased identifier, or <see langword="null" /> where <see cref="TrustedAuthenticationAuthority" /> refuses it or it is absent.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Host names and authserv-ids are written lower-case by convention, which is the form an operator reads in a listing; a header is still matched through TrustedAuthenticationAuthority's own upper-cased form, and a host is matched against another host lowered the same way.")]
    public static string? TryNormalizeAuthorityIdentifier(string? authorityIdentifier) =>
        authorityIdentifier is not null && TrustedAuthenticationAuthority.TryCreate(authorityIdentifier, out _)
            ? authorityIdentifier.Trim().ToLowerInvariant()
            : null;
}
