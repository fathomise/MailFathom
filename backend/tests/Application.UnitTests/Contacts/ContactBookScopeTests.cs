// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers which books a scope names and in what order, which is the precedence every read applies.</summary>
public sealed class ContactBookScopeTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-00000000f001");

    /// <summary>A member reads their own book first, their organization's next, and what their mailboxes collected last.</summary>
    /// <remarks>
    /// The order is the precedence: an address the member wrote down is answered from their own record, one the
    /// organization recorded from the organization's, and the collected record only where neither holds it.
    /// </remarks>
    [Fact]
    public void Of_AMemberOfAnOrganizationWithAnAssignedAccount_OrdersOwnThenOrganizationThenCollected()
    {
        // Act
        var scope = ContactBookScope.Of(SyntheticUser.Deployment, Organization, [SyntheticMailAccount.Deployment]);

        // Assert
        Assert.Equal(
            [
                ContactBookHolder.Of(SyntheticUser.Deployment).Key,
                ContactBookHolder.OfOrganization(Organization).Key,
                ContactBookHolder.Of(SyntheticMailAccount.Deployment).Key,
            ],
            scope.Keys);
        Assert.Equal(ContactBookHolder.OfOrganization(Organization), scope.OrganizationBook);
    }

    /// <summary>A user placed in no organization reads no organization's book.</summary>
    [Fact]
    public void Of_AUserInNoOrganization_NamesNoOrganizationBook()
    {
        // Act
        var scope = ContactBookScope.Of(SyntheticUser.Deployment, organization: null, [SyntheticMailAccount.Deployment]);

        // Assert
        Assert.Null(scope.OrganizationBook);
        Assert.DoesNotContain(
            scope.Keys,
            key => key.StartsWith(ContactBookHolder.OrganizationKeyPrefix, StringComparison.Ordinal));
    }

    /// <summary>What a member's own write may reach leaves the organization's book out, and keeps everything else.</summary>
    /// <remarks>
    /// A member reads the organization's contacts and never writes them, so an amendment or an erasure reached through
    /// their scope answers an organization's contact as one they do not hold rather than changing what every member reads.
    /// </remarks>
    [Fact]
    public void WithoutOrganizationBook_AMembersScope_KeepsTheirOwnBookAndTheCollectedOnes()
    {
        // Arrange
        var scope = ContactBookScope.Of(SyntheticUser.Deployment, Organization, [SyntheticMailAccount.Deployment]);

        // Act
        var writable = scope.WithoutOrganizationBook();

        // Assert
        Assert.Null(writable.OrganizationBook);
        Assert.Equal(
            [ContactBookHolder.Of(SyntheticUser.Deployment).Key, ContactBookHolder.Of(SyntheticMailAccount.Deployment).Key],
            writable.Keys);
    }

    /// <summary>An organization's scope writes into the organization's book and reads the collected books named beside it.</summary>
    [Fact]
    public void OfOrganization_AnAccountNamedBesideIt_OwnsTheOrganizationsBookAndNoUser()
    {
        // Act
        var scope = ContactBookScope.OfOrganization(Organization, [SyntheticMailAccount.Deployment]);

        // Assert
        Assert.Equal(ContactBookHolder.OfOrganization(Organization), scope.OwnBook);
        Assert.Equal([SyntheticMailAccount.Deployment], scope.Accounts);
        Assert.Throws<InvalidOperationException>(() => scope.User);
    }

    /// <summary>An organization's book holds what an administrator wrote down, under its own key.</summary>
    [Fact]
    public void OfOrganization_AnOrganization_IsAnAssertedBookKeyedByIt()
    {
        // Act
        var holder = ContactBookHolder.OfOrganization(Organization);

        // Assert
        Assert.Equal(ContactOrigin.Asserted, holder.Origin);
        Assert.Equal("organization:0198f0aa-0000-7000-8000-00000000f001", holder.Key);
        Assert.Equal(Organization, holder.Organization);
        Assert.Null(holder.User);
        Assert.Null(holder.Account);
    }

    /// <summary>The empty UUID names no organization, so it keys no book.</summary>
    [Fact]
    public void OfOrganization_TheEmptyIdentity_IsRefused()
    {
        // Act
        var refusal = Record.Exception(() => ContactBookHolder.OfOrganization(Guid.Empty));

        // Assert
        Assert.IsType<ArgumentException>(refusal, exactMatch: false);
    }
}
