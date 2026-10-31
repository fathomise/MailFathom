// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Contacts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Contacts;

/// <summary>Covers which writer each address row of an amended contact records, which is what decides whether it grants trust.</summary>
public sealed class ContactStoreTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AmendedAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly ContactBookHolder OwnBook = ContactBookHolder.Of(SyntheticUser.Deployment);

    private static readonly ContactAddressWriter Agent = new(ContactAddressWriterKind.Agent, SyntheticUser.Deployment, Credential: null);

    private static readonly ContactAddressWriter Person = new(ContactAddressWriterKind.Person, SyntheticUser.Deployment, Credential: Guid.Parse("0199f000-0000-7000-8000-0000000000e1"));

    /// <summary>
    /// A kept address the amendment restates records the amendment's writer and time, while a kept address it does not
    /// name keeps the writer an earlier write recorded.
    /// </summary>
    [Fact]
    public void ReplaceAddresses_OneKeptAddressRestated_RecordsTheNewWriterOnItAlone()
    {
        // Arrange
        var original = ContactOf(RecordedAt);
        var held = ContactMapping.ToEntity(OwnBook, original, Agent);
        var amended = ContactOf(AmendedAt, original.Id);
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        ContactStore.ReplaceAddresses(context, held, OwnBook, amended, Person, [Address("ANNA@example.test")]);

        // Assert
        var restated = Assert.Single(held.Addresses, address => address.NormalizedAddress == Address("anna@example.test").NormalizedAddress);
        var kept = Assert.Single(held.Addresses, address => address.NormalizedAddress == Address("anna@personal.test").NormalizedAddress);
        Assert.Equal((ContactAddressWriterKind.Person, Person.Credential, AmendedAt), (restated.WriterKind, restated.WriterCredentialId, restated.WrittenAt));
        Assert.Equal((ContactAddressWriterKind.Agent, (Guid?)null, RecordedAt), (kept.WriterKind, kept.WriterCredentialId, kept.WrittenAt));
    }

    /// <summary>An amendment's numbers replace the held rows position by position, and the positions it no longer fills go.</summary>
    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 0)]
    public void ReplacePhoneNumbers_AnAmendmentStatingAnotherList_LeavesExactlyItsRowsInItsOrder(int heldCount, int amendedCount)
    {
        // Arrange
        var original = ContactOf(RecordedAt, phoneNumbers: PhoneNumbersEndingIn(heldCount, "0"));
        var held = ContactMapping.ToEntity(OwnBook, original, Agent);
        var amended = ContactOf(AmendedAt, original.Id, PhoneNumbersEndingIn(amendedCount, "9"));
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        ContactStore.ReplacePhoneNumbers(context, held, amended);

        // Assert
        Assert.Equal(
            amended.PhoneNumbers.Select((phoneNumber, position) => (position, phoneNumber.Number, phoneNumber.Label)),
            held.PhoneNumbers.OrderBy(row => row.Position).Select(row => (row.Position, row.Number, row.Label)));
        Assert.All(held.PhoneNumbers, row => Assert.Equal(original.Id.Value, row.ContactId));
    }

    private static ContactPhoneNumber[] PhoneNumbersEndingIn(int count, string ending) =>
        [.. Enumerable.Range(0, count).Select(position => ContactPhoneNumber.Create(
            $"+1 202 555 01{position}{ending}",
            position % 2 == 0 ? ContactPhoneLabel.Mobile : ContactPhoneLabel.Work))];

    private static Contact ContactOf(
        DateTimeOffset amendedAt,
        ContactId? id = null,
        IReadOnlyCollection<ContactPhoneNumber>? phoneNumbers = null) =>
        Contact.Create(
            id ?? ContactId.Create(Guid.CreateVersion7(RecordedAt)),
            ContactDisplayName.Create("Anna Kowalska"),
            [Address("anna@example.test"), Address("anna@personal.test")],
            Address("anna@example.test"),
            note: null,
            ContactOrigin.Asserted,
            RecordedAt,
            amendedAt,
            phoneNumbers: phoneNumbers);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
}
