// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the route resolving a set of addresses to photos decides about a request and how the answer is shaped.</summary>
/// <remarks>Which photo an address is drawn by is <c>PersonPhotos</c>'s, covered there; what is asserted here is the part above it.</remarks>
public sealed class ClientPersonPhotoEndpointTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-00000000e201");

    private static readonly UserId Piotr = SyntheticUser.Another;

    private static readonly Guid PiotrsPortrait = new("0198f0aa-0000-7000-8000-00000000e2f1");

    private readonly IMemberDirectory members = Substitute.For<IMemberDirectory>();

    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();

    /// <summary>The deployment's half of an agreement with a client that composes this path from a constant of its own.</summary>
    [Fact]
    public void PersonPhotosRoute_IsAPathAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/contacts/photos", ClientPersonPhotoEndpoint.PersonPhotosRoute);
    }

    /// <summary>An address drawn by a photo is answered under the spelling the caller sent, and one drawn by initials or naming no mailbox is absent.</summary>
    [Fact]
    public async Task ResolveAsync_AColleaguesAddressBesideOthers_AnswersTheColleagueAlone()
    {
        // Arrange
        this.Holding(new MemberContact(
            Piotr,
            ContactDisplayName.Create("Piotr Zieliński"),
            [Mailbox("piotr@nordwind.example")],
            Mailbox("piotr@nordwind.example"),
            [],
            JobTitle: null,
            "Nordwind",
            StoredFileId.Create(PiotrsPortrait),
            TimeZone: null,
            WorkingHours: null));

        // Act
        var result = await ClientPersonPhotoEndpoint.ResolveAsync(
            new PersonPhotosRequest(["Piotr@Nordwind.example", "stranger@example.net", "not an address", null]),
            this.Photos(),
            TestContext.Current.CancellationToken);

        // Assert
        var answer = Assert.IsType<Ok<PersonPhotosResponse>>(result.Result).Value!;
        var entry = Assert.Single(answer.Photos);
        Assert.Equal("Piotr@Nordwind.example", entry.Address);
        Assert.Equal(new PersonPhotoResponse("Member", Piotr.Value, PiotrsPortrait), entry.Photo);
    }

    /// <summary>A request naming no list, and one naming more addresses than one resolution answers, are refused saying so.</summary>
    [Fact]
    public async Task ResolveAsync_NoListOrTooLongAList_IsRefused()
    {
        // Arrange
        string?[] tooMany = [.. Enumerable.Range(0, PersonPhotos.MaximumAddresses + 1).Select(at => $"sender{at}@example.org")];

        // Act
        var absent = await ClientPersonPhotoEndpoint.ResolveAsync(
            new PersonPhotosRequest(Addresses: null),
            this.Photos(),
            TestContext.Current.CancellationToken);
        var oversized = await ClientPersonPhotoEndpoint.ResolveAsync(
            new PersonPhotosRequest(tooMany),
            this.Photos(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(absent.Result).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(oversized.Result).StatusCode);
    }

    private static EmailAddress Mailbox(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return mailbox;
    }

    private void Holding(params MemberContact[] composed)
    {
        this.members.ReadAsync(Organization, Arg.Any<CancellationToken>()).Returns(composed);
        this.directory
            .FindHoldersOfAsync(Arg.Any<ContactBookHolder>(), Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());
    }

    private PersonPhotos Photos()
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.ContactsRead);
        var ownership = ContactBookOwnerships.For(
            authorization,
            new StubMailAccountAssignments(),
            new StatedAdministrativeTargets().WithUser(SyntheticUser.Deployment, Organization));

        return new PersonPhotos(this.directory, this.members, ownership, authorization);
    }
}
