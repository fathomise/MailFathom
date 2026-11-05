// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules;

/// <summary>How a caller names one rule of an account's set: by its identifier, or by its name within the account.</summary>
/// <remarks>
/// <para>
/// A person names a rule by what they read, and a script by what it stored, so both are accepted and the set decides
/// which rule was meant. The identifier is tried first; text that reads as one but names no rule of the set is then
/// matched as a name, because nothing stops a person from naming a rule with text that looks like an identifier.
/// </para>
/// <para>
/// It is resolved against the set a write has locked and checked the version of, never against an earlier reading, so
/// the rule a name resolves to is the rule the caller saw under it when they read that version.
/// </para>
/// </remarks>
public readonly record struct MailRuleReference
{
    private readonly string? text;

    private MailRuleReference(string text) => this.text = text;

    /// <summary>Gets the identifier or name as the caller wrote it, trimmed.</summary>
    public string Text => this.text ?? string.Empty;

    /// <summary>Names a rule by its identifier.</summary>
    /// <param name="rule">The identifier.</param>
    /// <returns>The reference.</returns>
    public static MailRuleReference To(MailRuleId rule) => new(rule.ToString());

    /// <summary>Reads the identifier or name a caller wrote.</summary>
    /// <param name="text">The identifier or the name.</param>
    /// <param name="reference">The reference, or the unusable default when the text names nothing.</param>
    /// <returns><see langword="true" /> when the text is not blank.</returns>
    public static bool TryCreate(string? text, out MailRuleReference reference)
    {
        reference = string.IsNullOrWhiteSpace(text) ? default : new MailRuleReference(text.Trim());

        return reference.text is not null;
    }

    /// <summary>Finds the rule this names among a set's rules.</summary>
    /// <param name="declarations">The set's rules.</param>
    /// <returns>The rule, or <see langword="null" /> when none carries the identifier or the name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <remarks>A name is matched without regard to case, as a set refuses two names that differ only in it.</remarks>
    public MailRuleDeclaration? FindIn(IReadOnlyList<MailRuleDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var written = this.Text;

        return (MailRuleId.TryParse(written, out var id) ? declarations.FirstOrDefault(rule => rule.Id == id) : null)
            ?? declarations.FirstOrDefault(rule => string.Equals(rule.Name?.Trim(), written, StringComparison.OrdinalIgnoreCase));
    }
}
