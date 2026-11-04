// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Domain.Mutations;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Writes a first outcome as the document its row keeps, and reads it back.</summary>
/// <remarks>
/// The document names a mutation and a lifecycle by the word every other surface publishes for it, so a row stays
/// readable in an ad-hoc query and survives a reordering of what the words stand for.
/// </remarks>
internal static class MailActKeyMapping
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string ToDocument(MailActOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return JsonSerializer.Serialize(
            new Document(
                outcome.Outcome,
                [.. outcome.Changes.Select(ToStoredChange)],
                [.. outcome.SpamMarkChanges.Select(ToStoredChange)],
                outcome.Account,
                outcome.Folder,
                outcome.Destination,
                outcome.IsMoved),
            Options);
    }

    internal static MailActOutcome ToOutcome(string document)
    {
        var stored = JsonSerializer.Deserialize<Document>(document, Options)
            ?? throw new JsonException("A stored first outcome is a document.");

        return new MailActOutcome(
            stored.Outcome,
            [.. stored.Changes.Select(ToChange)],
            [.. stored.SpamMarkChanges.Select(ToChange)],
            stored.Account,
            stored.Folder,
            stored.Destination,
            stored.IsMoved);
    }

    private static StoredChange ToStoredChange(RecordedMailFlagMutation change) =>
        new(change.Mutation.Name, change.RecordId.Value, change.Lifecycle.Name);

    private static RecordedMailFlagMutation ToChange(StoredChange stored) =>
        MailboxMutation.TryParseName(stored.Mutation, out var mutation)
        && MailboxMutationLifecycle.TryParseName(stored.Lifecycle, out var lifecycle)
            ? new RecordedMailFlagMutation(mutation, MailboxMutationRecordId.Create(stored.RecordId), lifecycle)
            : throw new JsonException("A stored first outcome names a mutation and a lifecycle this build publishes.");

    private sealed record Document(
        string Outcome,
        StoredChange[] Changes,
        StoredChange[] SpamMarkChanges,
        string? Account,
        string? Folder,
        string? Destination,
        bool IsMoved);

    private sealed record StoredChange(string Mutation, Guid RecordId, string Lifecycle);
}
