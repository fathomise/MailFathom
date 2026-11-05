// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>Serializes the stored forms of a rule and its actions without reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<StoredMailRuleDeclaration>))]
[JsonSerializable(typeof(IReadOnlyList<StoredMailRuleAction>))]
internal sealed partial class StoredMailRuleJsonContext : JsonSerializerContext;
