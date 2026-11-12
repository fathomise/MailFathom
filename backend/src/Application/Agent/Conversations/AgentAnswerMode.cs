// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>How far the run answering a question may go: how much mail it may read, and how many calls and tokens it may spend reading it.</summary>
/// <remarks>
/// <para>
/// Every mode answers on the same model with the same tools and the same plan; what differs is the ceiling the run is
/// held to. A fast run is held to the ceilings every answering run has, and a deep one to the wider ceilings the
/// deployment declares for it, so the cost of a mode is a number the operator set rather than a property of the model.
/// </para>
/// <para>
/// The mode is the person's to choose, and everybody who may ask the Agent anything may ask in any mode. What it does
/// not choose is anything about the period: a deep run is admitted against the same period, and what it spends is
/// counted there exactly as a fast one's is.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentAnswerMode>))]
public enum AgentAnswerMode
{
    /// <summary>Starts under the fast ceilings and moves to the deep ones at most once, where the run finds the question needs it.</summary>
    /// <remarks>The mode a question carries when it names none.</remarks>
    Auto = 0,

    /// <summary>Holds the run to the fast ceilings throughout; a run that reaches one answers from what it read and says it stopped there.</summary>
    Fast = 1,

    /// <summary>Holds the run to the deep ceilings from its first call.</summary>
    Deep = 2,
}
