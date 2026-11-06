// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Buffers;
using System.Text.Json;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>One rule an import file declares for the account being imported into.</summary>
/// <param name="Declaration">The rule object the deployment is handed, without <c>Enabled</c> or <c>Accounts</c>.</param>
/// <param name="Name">The name the file gives the rule, or <see langword="null" /> where it gives none.</param>
/// <param name="StatedEnabled">Whether the file states the rule ran, which an import reports and does not apply.</param>
internal sealed record ImportedRuleDeclaration(JsonElement Declaration, string? Name, bool StatedEnabled);

/// <summary>Reads the rules an import file declares for one account.</summary>
/// <remarks>
/// <para>
/// Two shapes are read, and they are one shape at two depths. The file <c>rules export</c> writes is an object whose
/// <c>Rules</c> is the account's rules; a configuration file is an object whose <c>MailRules</c> section holds a
/// <c>Rules</c> list the way the retired configuration declared it. Every key outside those two lists is not read: in a
/// configuration file it is the rest of the deployment's configuration, and in the section it is the limits that stay
/// configuration. Keys are matched ignoring case, as configuration matches them, and a file may carry the comments and
/// trailing commas a configuration file may.
/// </para>
/// <para>
/// A configured rule could name the accounts it applied to. Such a rule is taken when its <c>Accounts</c> names the account
/// being imported into or names none, and left out otherwise; an exported rule names none. <c>Enabled</c> is read and
/// stripped: an imported rule the account does not have yet arrives disabled whatever the file says, so what the file
/// states is only reported, defaulting to <see langword="true" /> because that is what an unstated configured rule meant.
/// Everything else in a rule is the deployment's to read and refuse, so nothing here judges it.
/// </para>
/// </remarks>
internal static class RuleImportFile
{
    private const string MailRulesKey = "MailRules";

    private const string RulesKey = "Rules";

    private const string AccountsKey = "Accounts";

    private const string EnabledKey = "Enabled";

    private const string NameKey = "Name";

    private static readonly JsonDocumentOptions ConfigurationSyntax = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Reads the rules a file declares for one account, in the order it declares them.</summary>
    /// <param name="written">The file's text.</param>
    /// <param name="origin">Where the text came from, as a refusal names it.</param>
    /// <param name="account">The account being imported into, which a configured rule's <c>Accounts</c> is matched against.</param>
    /// <returns>The rules for that account, empty where the file declares none for it.</returns>
    /// <exception cref="CliFailure">Thrown when the text is not JSON, holds no rules list, or a rule is not one this can read.</exception>
    /// <remarks>Nothing the file holds is repeated in a refusal: a rule is named by its place in the list, from <c>#1</c>.</remarks>
    internal static IReadOnlyList<ImportedRuleDeclaration> Read(string written, string origin, string account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var document = Parse(written, origin);

        var root = document.RootElement;
        var section = Property(root, MailRulesKey) is { ValueKind: JsonValueKind.Object } mailRules ? mailRules : root;

        if (Property(section, RulesKey) is not { ValueKind: JsonValueKind.Array } rules)
        {
            throw new CliFailure(
                $"{origin} holds no rules list: it is neither a file 'rules export' wrote, whose Rules lists the rules, nor a configuration file whose MailRules section declares Rules.");
        }

        return [.. rules.EnumerateArray().Select((rule, index) => Read(rule, $"#{index + 1}", origin, account.Trim())).OfType<ImportedRuleDeclaration>()];
    }

    private static ImportedRuleDeclaration? Read(JsonElement rule, string label, string origin, string account)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            throw new CliFailure($"Rule {label} in {origin} is not a JSON object, and a rule is one object.");
        }

        return NamesAccount(rule, label, origin, account)
            ? new ImportedRuleDeclaration(
                Without(rule, AccountsKey, EnabledKey),
                Property(rule, NameKey) is { ValueKind: JsonValueKind.String } name ? name.GetString() : null,
                StatedEnabled(rule, label, origin))
            : null;
    }

    private static bool NamesAccount(JsonElement rule, string label, string origin, string account) => Property(rule, AccountsKey) switch
    {
        null or { ValueKind: JsonValueKind.Null } => true,
        { ValueKind: JsonValueKind.Array } accounts when accounts.EnumerateArray().All(named => named.ValueKind == JsonValueKind.String) =>
            accounts.GetArrayLength() == 0
            || accounts.EnumerateArray().Any(named => string.Equals(named.GetString()?.Trim(), account, StringComparison.Ordinal)),
        _ => throw new CliFailure($"Rule {label} in {origin} has an Accounts that is not a list of account identifiers."),
    };

    private static bool StatedEnabled(JsonElement rule, string label, string origin) => Property(rule, EnabledKey) switch
    {
        null or { ValueKind: JsonValueKind.Null } => true,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.String } text when bool.TryParse(text.GetString(), out var stated) => stated,
        _ => throw new CliFailure($"Rule {label} in {origin} has an Enabled that is neither true nor false."),
    };

    /// <summary>Finds a property ignoring case, as configuration keys are matched.</summary>
    private static JsonElement? Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        ? element.EnumerateObject()
            .Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(property => (JsonElement?)property.Value)
            .FirstOrDefault()
        : null;

    /// <summary>Copies a rule object leaving out the keys the deployment does not read from one.</summary>
    private static JsonElement Without(JsonElement rule, params string[] keys)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (var property in rule.EnumerateObject().Where(property => !keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var copy = JsonDocument.Parse(buffer.WrittenMemory);

        return copy.RootElement.Clone();
    }

    private static JsonDocument Parse(string written, string origin)
    {
        try
        {
            return JsonDocument.Parse(written, ConfigurationSyntax);
        }
        catch (JsonException failure)
        {
            throw new CliFailure(
                $"{origin} is not a JSON document, so nothing was sent. It stops being one at line {(failure.LineNumber ?? 0) + 1}, byte {(failure.BytePositionInLine ?? 0) + 1}.",
                failure);
        }
    }
}
