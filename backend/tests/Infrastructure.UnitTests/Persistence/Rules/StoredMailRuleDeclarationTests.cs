// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Rules;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Rules;

/// <summary>
/// Covers the JSON the archive and the rule rows keep, which has to read back into exactly the declaration it was written
/// from: a field lost on the way is a rule that silently stops doing what its author wrote.
/// </summary>
public sealed class StoredMailRuleDeclarationTests
{
    private static readonly IReadOnlyList<MailRuleAction> EveryActionKind =
    [
        MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("archive"))),
        MailRuleAction.Copy(MailFolderReference.ToRole(MailFolderSpecialUse.Junk)),
        MailRuleAction.Delete(),
        MailRuleAction.SetSeen(true),
        MailRuleAction.SetFlagged(false),
        MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["invoice", "supplier"])),
        MailRuleAction.RemoveKeywords(AuthoredMailKeywords.Create(["unread-later"])),
        MailRuleAction.SetKeywords(AuthoredMailKeywords.Create(["filed"])),
    ];

    /// <summary>Every part of every declaration survives the archive's document, in the order the set holds them.</summary>
    [Fact]
    public void DeserializeSet_ASerializedSet_ReadsBackEveryDeclarationFieldByField()
    {
        // Arrange
        Assert.True(JobRecurrence.TryParse("Daily at 03:00 Europe/Warsaw", out var nightly, out _));
        IReadOnlyList<MailRuleDeclaration> declarations =
        [
            new(
                MailRuleId.Create(Guid.Parse("8a2f1c3e-0b4d-4e6f-9a1b-2c3d4e5f6a7b")),
                "file-invoices",
                Enabled: true,
                "senderDomain == 'supplier.example'",
                EveryActionKind,
                StopWhenMatched: true,
                [MailRuleTrigger.Arrival],
                Schedule: null),
            new(
                MailRuleId.Create(Guid.Parse("1b2c3d4e-5f6a-4b7c-8d9e-0f1a2b3c4d5e")),
                "archive-old-newsletters",
                Enabled: false,
                "ageInDays > 30",
                [MailRuleAction.Delete()],
                StopWhenMatched: false,
                [MailRuleTrigger.Arrival, MailRuleTrigger.Schedule],
                nightly),
        ];

        // Act
        var read = StoredMailRuleDeclaration.DeserializeSet(StoredMailRuleDeclaration.SerializeSet(declarations));

        // Assert
        Assert.Equal(declarations.Select(declaration => declaration.Id), read.Select(declaration => declaration.Id));
        Assert.Equal(declarations.Select(declaration => declaration.Name), read.Select(declaration => declaration.Name));
        Assert.Equal(declarations.Select(declaration => declaration.Enabled), read.Select(declaration => declaration.Enabled));
        Assert.Equal(
            declarations.Select(declaration => declaration.ConditionText),
            read.Select(declaration => declaration.ConditionText));
        Assert.Equal(
            declarations.Select(declaration => declaration.StopWhenMatched),
            read.Select(declaration => declaration.StopWhenMatched));
        Assert.Equal(
            declarations.Select(declaration => string.Join(',', declaration.Triggers.Select(trigger => trigger.Name))),
            read.Select(declaration => string.Join(',', declaration.Triggers.Select(trigger => trigger.Name))));
        Assert.Equal(
            declarations.Select(declaration => declaration.Schedule?.CanonicalForm),
            read.Select(declaration => declaration.Schedule?.CanonicalForm));
        Assert.Equal(EveryActionKind, read[0].Actions);
        Assert.Equal([MailRuleAction.Delete()], read[1].Actions);
    }

    /// <summary>A set nobody wrote a rule into is an empty document, and reads back as an empty set rather than a refusal.</summary>
    [Fact]
    public void DeserializeSet_AnEmptySerializedSet_ReadsBackNoDeclarations()
    {
        // Act
        var read = StoredMailRuleDeclaration.DeserializeSet(StoredMailRuleDeclaration.SerializeSet([]));

        // Assert
        Assert.Empty(read);
    }

    /// <summary>A rule row keeps its actions on their own, and every kind a rule can take reads back as it was written.</summary>
    [Fact]
    public void DeserializeActions_SerializedActionsOfEveryKind_ReadBackInTheirDeclaredOrder()
    {
        // Act
        var read = StoredMailRuleDeclaration.DeserializeActions(StoredMailRuleDeclaration.SerializeActions(EveryActionKind));

        // Assert
        Assert.Equal(EveryActionKind, read);
        Assert.Equal(
            EveryActionKind.Select(action => action.CanonicalForm),
            read.Select(action => action.CanonicalForm));
    }

    /// <summary>A document naming an action this build does not know is refused rather than read as some other action.</summary>
    [Fact]
    public void DeserializeActions_AnUnknownMutation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => StoredMailRuleDeclaration.DeserializeActions(
            """[{"mutation":"forward","destination":null,"desiredState":null,"keywords":null}]"""));
    }

    /// <summary>A relocation without the folder it files into cannot be run, so it is refused rather than read.</summary>
    [Fact]
    public void DeserializeActions_ARelocationNamingNoFolder_IsRefused()
    {
        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => StoredMailRuleDeclaration.DeserializeActions(
            """[{"mutation":"relocate","destination":null,"desiredState":null,"keywords":null}]"""));
    }

    /// <summary>A row that names no schedule is a rule without one.</summary>
    [Fact]
    public void ReadSchedule_NoDeclaration_ReadsNoRecurrence()
    {
        // Act
        var schedule = StoredMailRuleDeclaration.ReadSchedule(declaration: null);

        // Assert
        Assert.Null(schedule);
    }

    /// <summary>A schedule this build cannot read would otherwise run at some other time than the author wrote.</summary>
    [Fact]
    public void ReadSchedule_AnUnreadableDeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => StoredMailRuleDeclaration.ReadSchedule("Whenever it suits"));
    }

    /// <summary>A rule naming no trigger is one only a requested run reaches, and reads back with none.</summary>
    [Fact]
    public void ReadTriggers_NoNames_ReadsNoTriggers()
    {
        // Act
        var triggers = StoredMailRuleDeclaration.ReadTriggers([]);

        // Assert
        Assert.Empty(triggers);
    }

    /// <summary>A trigger this build does not know is refused rather than dropped, which would quietly stop the rule.</summary>
    [Fact]
    public void ReadTriggers_AnUnknownName_IsRefused()
    {
        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => StoredMailRuleDeclaration.ReadTriggers(["Arrival", "Lunar"]));
    }
}
