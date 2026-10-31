// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.ThreadStates;

/// <summary>The shape a thread-state agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes a statement. Every field is optional and every value is untrusted:
/// what a model wrote is read into this and then validated into
/// <see cref="Application.Emails.ThreadStates.ThreadStateEntry" />, which is the type the rest of the system works
/// with.
/// </remarks>
internal sealed record ThreadStateDocument
{
    /// <summary>Gets what the model said the conversation settled.</summary>
    [JsonPropertyName("agreements")]
    public IReadOnlyList<ThreadStateEntryDocument?>? Agreements { get; init; }

    /// <summary>Gets what the model said the conversation left open.</summary>
    [JsonPropertyName("openQuestions")]
    public IReadOnlyList<ThreadStateEntryDocument?>? OpenQuestions { get; init; }

    /// <summary>Gets what the model said somebody undertook.</summary>
    [JsonPropertyName("commitments")]
    public IReadOnlyList<ThreadStateEntryDocument?>? Commitments { get; init; }

    /// <summary>Gets how the model said a document the conversation exchanged changed between versions.</summary>
    [JsonPropertyName("differences")]
    public IReadOnlyList<ThreadStateEntryDocument?>? Differences { get; init; }

    /// <summary>Gets what the model said the conversation is called.</summary>
    [JsonPropertyName("title")]
    public ThreadTitleDocument? Title { get; init; }
}

/// <summary>The title as the model wrote it: a topic or the answer that the one it was shown stays, and a standing.</summary>
internal sealed record ThreadTitleDocument
{
    /// <summary>Gets the new topic the model wrote.</summary>
    [JsonPropertyName("topic")]
    public string? Topic { get; init; }

    /// <summary>Gets whether the model answered that the topic it was shown stays.</summary>
    [JsonPropertyName("keepTopic")]
    public bool? KeepTopic { get; init; }

    /// <summary>Gets whether the model stated that the conversation moved on to another subject.</summary>
    [JsonPropertyName("subjectChanged")]
    public bool? SubjectChanged { get; init; }

    /// <summary>Gets where the model said the conversation stands.</summary>
    [JsonPropertyName("standing")]
    public string? Standing { get; init; }
}

/// <summary>One statement as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// The messages are the numbers the turn gave them rather than identifiers, which is why they are integers here: a
/// number outside the range the turn published names no message and the statement falls away with it.
/// </remarks>
internal sealed record ThreadStateEntryDocument
{
    /// <summary>Gets the statement itself.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Gets the turn's own numbers for the messages the statement rests on.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<int>? Messages { get; init; }

    /// <summary>Gets who the model said owes the commitment.</summary>
    [JsonPropertyName("owedBy")]
    public string? OwedBy { get; init; }

    /// <summary>Gets when the model said the commitment falls due.</summary>
    [JsonPropertyName("dueAt")]
    public string? DueAt { get; init; }
}
