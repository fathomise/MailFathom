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

    private static Contact ContactOf(DateTimeOffset amendedAt, ContactId? id = null) =>
        Contact.Create(
            id ?? ContactId.Create(Guid.CreateVersion7(RecordedAt)),
            ContactDisplayName.Create("Anna Kowalska"),
            [Address("anna@example.test"), Address("anna@personal.test")],
            Address("anna@example.test"),
            note: null,
            ContactOrigin.Asserted,
            RecordedAt,
            amendedAt);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
}
