// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.KnownProviders;

/// <summary>The options the known-provider commands share.</summary>
internal static class KnownProviderOptions
{
    /// <summary>Builds the option naming the IMAP host an entry is matched by.</summary>
    /// <returns>The option.</returns>
    internal static Option<string> ImapHost() => new("--host")
    {
        Description = "The IMAP host accounts synchronize from, exactly as they connect to it; matched without regard to case, never by suffix or wildcard.",
        Required = true,
    };
}
