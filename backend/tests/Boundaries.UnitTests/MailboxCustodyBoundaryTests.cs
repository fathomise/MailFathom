// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Domain.Accounts;
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
/// its folders. Each implementation of the port is outside that set on purpose.
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
