// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Decisions;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers how a member contact is composed from the row and the record of the user it stands for.</summary>
public sealed class MemberDirectoryTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-00000000d001");

    private static readonly UserId Piotr = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000d002"));

    private static readonly Guid PiotrsPortrait = new("0198f0aa-0000-7000-8000-00000000d0f1");

    private readonly IOrganizationMemberReader reader = Substitute.For<IOrganizationMemberReader>();

    /// <summary>What the row holds and what the record states meet in one contact, and nothing else is read to compose it.</summary>
    [Fact]
    public async Task ReadAsync_AMemberWithARecord_ComposesTheContactFromTheRowAndTheRecord()
    {
        // Arrange
        this.Reading(new OrganizationMember(
            Piotr,
            "Piotr Zieliński",
            "Controller",
            $$"""
              {"Language": "English", "TimeZone": "Europe/Warsaw", "Portrait": "{{PiotrsPortrait:D}}",
               "PreferredAddress": "P.Zielinski@nordwind.example",
               "PhoneNumbers": [{"Number": "+48 600 100 200", "Label": "Mobile"}]}
              """,
            ["piotr@nordwind.example", "p.zielinski@nordwind.example"]));

        // Act
        var member = Assert.Single(await this.Directory().ReadAsync(Organization, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Piotr, member.Member);
        Assert.Equal("Piotr Zieliński", member.DisplayName.Value);
        Assert.Equal(["piotr@nordwind.example", "p.zielinski@nordwind.example"], member.Addresses.Select(address => address.Address));
        Assert.Equal("p.zielinski@nordwind.example", member.PreferredAddress?.Address);
        Assert.Equal("+48 600 100 200", Assert.Single(member.PhoneNumbers).Number);
        Assert.Equal("Controller", member.JobTitle);
        Assert.Equal("Nordwind", member.OrganizationName);
        Assert.Equal(PiotrsPortrait, member.Portrait?.Value);
        Assert.Equal("Europe/Warsaw", member.TimeZone?.Id);
    }

    /// <summary>
    /// A member's own record never takes them out of their colleagues' directory: one past the bound, or one that does
    /// not bind, still yields the member with what their row holds.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("""{"Language": "Klingon"}""")]
    public async Task ReadAsync_AMemberWhoseRecordDoesNotBind_IsStillListedFromTheRow(string? json)
    {
        // Arrange
        this.Reading(new OrganizationMember(Piotr, "Piotr Zieliński", JobTitle: null, json, ["piotr@nordwind.example"]));

        // Act
        var member = Assert.Single(await this.Directory().ReadAsync(Organization, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("piotr@nordwind.example", member.PreferredAddress?.Address);
        Assert.Empty(member.PhoneNumbers);
        Assert.Null(member.Portrait);
    }

    /// <summary>A record naming the all-zero portrait names none, so it reads as a member with no portrait rather than failing the directory.</summary>
    [Fact]
    public async Task ReadAsync_AMemberWhoseRecordNamesTheEmptyPortrait_IsListedWithNoPortrait()
    {
        // Arrange
        this.Reading(new OrganizationMember(
            Piotr,
            "Piotr Zieliński",
            JobTitle: null,
            """{"Language": "English", "TimeZone": "Europe/Warsaw", "Portrait": "00000000-0000-0000-0000-000000000000"}""",
            ["piotr@nordwind.example"]));

        // Act
        var member = Assert.Single(await this.Directory().ReadAsync(Organization, TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(member.Portrait);
        Assert.Equal("Europe/Warsaw", member.TimeZone?.Id);
    }

    /// <summary>A label no contact could carry as a name leaves the member out, rather than failing the whole directory.</summary>
    [Fact]
    public async Task ReadAsync_AMemberWhoseLabelNoContactCouldCarry_IsLeftOut()
    {
        // Arrange
        this.Reading(
            new OrganizationMember(Piotr, "Piotr\u0007", JobTitle: null, Json: null, []),
            new OrganizationMember(UserId.Create(Guid.CreateVersion7()), "Zofia Nowak", JobTitle: null, Json: null, []));

        // Act
        var members = await this.Directory().ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Zofia Nowak"], members.Select(member => member.DisplayName.Value));
    }

    /// <summary>An organization the store holds no row for has no members, which is an empty directory rather than a failure.</summary>
    [Fact]
    public async Task ReadAsync_AnOrganizationTheStoreDoesNotHold_IsAnEmptyDirectory()
    {
        // Arrange
        this.reader.ReadAsync(Organization, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((OrganizationMembers?)null);

        // Act
        var members = await this.Directory().ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(members);
    }

    /// <summary>One member is composed from the one row the reader returns, through the same composition as a listing.</summary>
    [Fact]
    public async Task FindAsync_AMemberTheReaderReturns_IsComposedFromTheirRow()
    {
        // Arrange
        this.reader
            .ReadMemberAsync(Organization, Piotr, Arg.Any<CancellationToken>())
            .Returns(new OrganizationMembers(
                "Nordwind",
                GoverningSettingsPolicies.Unwritten,
                [new OrganizationMember(Piotr, "Piotr Zieliński", "Controller", Json: null, ["piotr@nordwind.example"])]));

        // Act
        var member = await this.Directory().FindAsync(Organization, Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Piotr Zieliński", member?.DisplayName.Value);
        Assert.Equal("piotr@nordwind.example", member?.PreferredAddress?.Address);
        await this.reader.DidNotReceiveWithAnyArgs().ReadAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A user the reader returns no row for — one of another organization, a service account, or nobody — is no member,
    /// and so is any user of an organization the store does not hold.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindAsync_AUserTheReaderReturnsNoRowFor_IsNoMember(bool organizationHeld)
    {
        // Arrange
        this.reader
            .ReadMemberAsync(Organization, Piotr, Arg.Any<CancellationToken>())
            .Returns(organizationHeld ? new OrganizationMembers("Nordwind", GoverningSettingsPolicies.Unwritten, []) : null);

        // Act
        var member = await this.Directory().FindAsync(Organization, Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(member);
    }

    private void Reading(params OrganizationMember[] members) =>
        this.reader
            .ReadAsync(Organization, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationMembers("Nordwind", GoverningSettingsPolicies.Unwritten, members));

    private MemberDirectory Directory() =>
        new(
            this.reader,
            new UserAccountDocumentBinder(
                new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                new FakeTimeProvider(),
                Options.Create(new SensitiveContentOptions()),
                Options.Create(new DecisionModelOptions())));
}
