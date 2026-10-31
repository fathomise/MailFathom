// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>Names which of a person's phones a number reaches.</summary>
/// <remarks>
/// A closed set rather than free text, so every surface draws the same four choices and a client can offer them as a
/// list. The names are published as they are spelled here, so renaming one is a change to the contract.
/// </remarks>
public enum ContactPhoneLabel
{
    /// <summary>The phone the person carries.</summary>
    Mobile = 0,

    /// <summary>The phone the person is reached on at work.</summary>
    Work = 1,

    /// <summary>The phone the person is reached on at home.</summary>
    Home = 2,

    /// <summary>A phone none of the other labels names.</summary>
    Other = 3,
}
