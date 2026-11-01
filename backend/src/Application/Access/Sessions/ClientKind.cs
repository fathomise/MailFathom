// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Sessions;

/// <summary>Which of MailFathom's heads a client session was signed in from, as the head declared it at sign-in.</summary>
/// <remarks>
/// Declared and never verified, which is enough for what reads it: it marks a member as using the phone app and grants
/// nothing. A sign-in that declares nothing is <see cref="Web" />.
/// </remarks>
public enum ClientKind
{
    /// <summary>The client served to a browser.</summary>
    Web = 0,

    /// <summary>The desktop shell.</summary>
    Desktop = 1,

    /// <summary>The phone app on Android.</summary>
    Android = 2,
}
