// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Resolves every address to its initials, for a mail route whose test is about anything but a sender's photo.</summary>
/// <remarks>
/// The real resolution under a caller holding no contact grant, which answers with nothing before it reads a book or a
/// directory — so a route test reaches the same code a deployment runs without arranging either.
/// </remarks>
internal static class UnresolvedPersonPhotos
{
    /// <summary>Composes the resolution.</summary>
    /// <returns>A resolution that names no photo.</returns>
    internal static PersonPhotos Create()
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        return new PersonPhotos(
            Substitute.For<IContactDirectory>(),
            Substitute.For<IMemberDirectory>(),
            new ContactBookOwnership(
                authorization,
                new ContactBookScopes(new StubMailAccountAssignments(), new StatedAdministrativeTargets())),
            authorization);
    }
}
