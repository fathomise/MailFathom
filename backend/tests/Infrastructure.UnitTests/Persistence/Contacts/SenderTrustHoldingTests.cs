// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Persistence.Contacts;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Contacts;

/// <summary>Covers which of the holdings a read of the books returned recognize one account's sender.</summary>
public sealed class SenderTrustHoldingTests
{
    private const string Account = "0199f000-0000-7000-8000-0000000000a1";

    private const string OtherAccount = "0199f000-0000-7000-8000-0000000000a2";

    private const string Address = "ALICE@PARTNER.EXAMPLE";

    private static readonly Guid FirstContact = Guid.Parse("0199f000-0000-7000-8000-0000000000c1");

    private static readonly Guid SecondContact = Guid.Parse("0199f000-0000-7000-8000-0000000000c2");

    /// <summary>An address a person or an administrator wrote down recognizes the sender, under the book that holds it.</summary>
    [Theory]
    [InlineData(ContactAddressWriterKind.Person, false)]
    [InlineData(ContactAddressWriterKind.Administrator, false)]
    [InlineData(ContactAddressWriterKind.Person, true)]
    [InlineData(ContactAddressWriterKind.Administrator, true)]
    public void RecognizingContactsOf_AddressAPersonOrAnAdministratorWrote_NamesTheContactUnderItsBook(
        ContactAddressWriterKind writer,
        bool isOrganizationBook)
    {
        // Act
        var contacts = SenderTrustHolding.RecognizingContactsOf(
            [new SenderTrustHolding(Account, Address, FirstContact, isOrganizationBook, writer)],
            Account,
            Address);

        // Assert
        var expected = ContactId.Create(FirstContact);
        Assert.Equal(isOrganizationBook ? new RecognizingContacts(expected, null) : new RecognizingContacts(null, expected), contacts);
    }

    /// <summary>An address an agent wrote, or one recorded before writers were, is a record and never a grant.</summary>
    [Theory]
    [InlineData(ContactAddressWriterKind.Agent)]
    [InlineData(ContactAddressWriterKind.Unrecorded)]
    public void RecognizingContactsOf_AddressNobodyWithAuthorityWrote_RecognizesNobody(ContactAddressWriterKind writer)
    {
        // Act
        var contacts = SenderTrustHolding.RecognizingContactsOf(
            [
                new SenderTrustHolding(Account, Address, FirstContact, IsOrganizationBook: true, writer),
                new SenderTrustHolding(Account, Address, SecondContact, IsOrganizationBook: false, writer),
            ],
            Account,
            Address);

        // Assert
        Assert.Equal(RecognizingContacts.None, contacts);
    }

    /// <summary>A contact in a book only another account reaches says nothing about mail this account received.</summary>
    [Fact]
    public void RecognizingContactsOf_ContactInABookOnlyAnotherAccountReaches_RecognizesNobody()
    {
        // Act
        var contacts = SenderTrustHolding.RecognizingContactsOf(
            [new SenderTrustHolding(OtherAccount, Address, FirstContact, IsOrganizationBook: false, ContactAddressWriterKind.Person)],
            Account,
            Address);

        // Assert
        Assert.Equal(RecognizingContacts.None, contacts);
    }

    /// <summary>A contact holding another sender's address says nothing about this one, though one read returned both.</summary>
    [Fact]
    public void RecognizingContactsOf_ContactHoldingAnotherAddress_RecognizesNobody()
    {
        // Act
        var contacts = SenderTrustHolding.RecognizingContactsOf(
            [new SenderTrustHolding(Account, "MALLORY@PARTNER.EXAMPLE", FirstContact, IsOrganizationBook: true, ContactAddressWriterKind.Person)],
            Account,
            Address);

        // Assert
        Assert.Equal(RecognizingContacts.None, contacts);
    }

    /// <summary>Two contacts holding the address in one book recognize the sender as the same one whichever row arrived first.</summary>
    [Fact]
    public void RecognizingContactsOf_TwoContactsHoldTheAddressInOneBook_NamesTheSameOneEitherWay()
    {
        // Arrange
        SenderTrustHolding[] holdings =
        [
            new(Account, Address, SecondContact, IsOrganizationBook: false, ContactAddressWriterKind.Person),
            new(Account, Address, FirstContact, IsOrganizationBook: false, ContactAddressWriterKind.Person),
        ];

        // Act
        var forward = SenderTrustHolding.RecognizingContactsOf(holdings, Account, Address);
        var reversed = SenderTrustHolding.RecognizingContactsOf([.. holdings.Reverse()], Account, Address);

        // Assert
        Assert.Equal(ContactId.Create(FirstContact), forward.PersonalContact);
        Assert.Equal(forward, reversed);
    }
}
