// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers which types may read an account's custody phase, so an act is dispatched by it in one place only.</summary>
/// <remarks>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md" />
/// makes <see cref="MailboxCustodianResolver" /> the only reader of the phase for an act: every act on a mailbox is
/// carried by the one custodian the resolver chooses, and a check of the phase anywhere else is a second dispatch that
/// drifts from the first. Reflection cannot hold that, because a phase compared inside a method body and never stored
/// appears in no signature. The types admitted here either decide from the phase because they exist for one mode — the
/// synchronization, the drain, the restore, and the converger — or own it rather than decide from it: the custody domain
/// types, the store and the switch that change it, the endpoint that reports it, and the persistence of the account and
/// its folders. Each implementation of the port is outside that set on purpose. The same dispatch can be written without
/// naming the phase, as a test of which custodian the resolver returned, so the concrete custodians are held to their
/// own namespace and the registration that composes them.
/// </remarks>
public sealed class MailboxCustodyBoundaryTests
{
    private const string PhaseReadersPattern =
        @"^MailFathom\.("
        + @"Domain\.Accounts\."
        + @"|Application\.Accounts\.Custody\.(MailboxCustodianResolver|IMailAccountCustodyStore|MailAccountCustodySwitch)"
        + @"|Application\.Synchronization\."
        + @"|Application\.Mail\.Mutations\.Convergence\."
        + @"|Infrastructure\.Persistence\.(Accounts|Folders|Synchronization|Entities|Migrations)\."
        + @"|Host\.Api\.MailAccountCustodyEndpoints"
        + @")";

    private const string ConcreteCustodianReadersPattern =
        @"^MailFathom\.(Application\.Accounts\.Custody\.|Infrastructure\.ServiceCollectionExtensions)";

    [Fact]
    public void CustodyPhase_OutsideItsReadersAndOwners_IsUnreachable()
    {
        // Arrange
        IArchRule onlyTheResolverDispatchesAnAct = Types()
            .That()
            .DependOnAny(typeof(MailAccountCustodyPhase))
            .Should()
            .HaveFullNameMatching(PhaseReadersPattern)
            .Because(
                "MailboxCustodianResolver is the one place an act is dispatched by the custody phase, so a custodian, a "
                    + "use case, or a collaborator reading it as well would decide the same act twice and drift from "
                    + "the resolver the first time a phase is added or its meaning changes");

        // Act & Assert
        onlyTheResolverDispatchesAnAct.Check(CompiledBoundaries.Solution);
    }

    [Fact]
    public void ConcreteCustodians_OutsideTheirNamespaceAndTheirRegistration_AreUnreachable()
    {
        // Arrange
        IArchRule onlyThePortIsAsked = Types()
            .That()
            .DependOnAny(typeof(MirroredMailboxCustodian), typeof(HeldMailboxCustodian), typeof(RestoringMailboxCustodian))
            .Should()
            .HaveFullNameMatching(ConcreteCustodianReadersPattern)
            .Because(
                "a caller testing which custodian it got back is a phase switch written as a type test, which a new "
                    + "custodian would silently fail; it asks IMailboxCustodian instead, so every custodian has to answer");

        // Act & Assert
        onlyThePortIsAsked.Check(CompiledBoundaries.Solution);
    }

    /// <summary>
    /// The rule above passes vacuously if a custodian named only inside a method body is not seen. The registration names
    /// each one in a call and in no signature, field, or base type, so it must be.
    /// </summary>
    [Fact]
    public void ConcreteCustodians_NamedOnlyInsideAMethodBody_AreSeenAsADependency()
    {
        // Act
        var readers = Types()
            .That()
            .DependOnAny(typeof(HeldMailboxCustodian))
            .GetObjects(CompiledBoundaries.Solution);

        // Assert
        Assert.Contains(readers, type => type.FullName == typeof(ServiceCollectionExtensions).FullName);
    }

    /// <summary>The rule above passes vacuously if a phase compared in a method body is not seen, so the resolver's own comparison must be.</summary>
    [Fact]
    public void CustodyPhase_ComparedInsideTheResolver_IsSeenAsADependency()
    {
        // Act
        var readers = Types()
            .That()
            .DependOnAny(typeof(MailAccountCustodyPhase))
            .GetObjects(CompiledBoundaries.Solution);

        // Assert
        Assert.Contains(readers, type => type.FullName.StartsWith(typeof(MailboxCustodianResolver).FullName!, StringComparison.Ordinal));
    }
}
