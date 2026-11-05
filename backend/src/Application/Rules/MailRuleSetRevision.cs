// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Application.Rules;

/// <summary>Identifies what one mail account's rules mean, as a digest of its enabled rules in the order they are evaluated.</summary>
/// <remarks>
/// <para>
/// Derived rather than declared, which is what makes it trustworthy: an authored version key can be forgotten, and an
/// edit that changes the rules while leaving the key alone is exactly the case a history has to be able to tell apart.
/// The set's version counts writes and is what a concurrent write is compared against; this names what the set means
/// and is what a run is bound to. A rename moves the version and leaves this alone.
/// </para>
/// <para>
/// The digest is taken over one account's enabled rules and over what selects mail and what happens to it, so it does
/// not move for the wrong reasons: renaming a rule changes what a person reads and nothing about which mail it reaches,
/// so the identifier stands in for the name. Reordering the rules does move it, because evaluation order is part of
/// what a rule set means. So do the triggers — a rule withdrawn from firing on arrival reaches different mail — and the
/// schedule beside them. So do the actions, for the reason the revision is part of a request's identity at all — a rule
/// now filing into a different folder must ask afresh rather than be answered by the record of the filing it asked for
/// before the edit. A disabled rule is left out, so disabling one moves the revision exactly as removing it would, and
/// re-enabling it unchanged returns the set to a revision it has had before.
/// </para>
/// <para>
/// It carries none of the authored text and no ordering. A record naming a revision therefore holds nothing personal
/// that the condition contributed, and a record that has to say which of two revisions came first carries its own
/// timestamp rather than inferring one from the value. Two accounts holding the same text under different identifiers
/// have different revisions, which is right: a revision means something only beside the account it belongs to.
/// </para>
/// </remarks>
public readonly record struct MailRuleSetRevision
{
    /// <summary>How many hexadecimal characters of the digest the identity keeps.</summary>
    /// <remarks>
    /// Twelve characters are forty-eight bits, which is far more than enough to tell apart the revisions one account's
    /// rule set passes through, and short enough to read in a log line without being elided.
    /// </remarks>
    private const int LengthInCharacters = 12;

    /// <summary>Separates the parts of one rule inside the digest input, and one rule from the next.</summary>
    /// <remarks>
    /// Unit and record separators rather than punctuation, because neither can occur in an identifier or a condition. A
    /// separator a value could contain would let two different rule sets render identically, which is the one way a
    /// derived identity can quietly stop distinguishing what it exists to distinguish.
    /// </remarks>
    private const char FieldSeparator = '\u001F';
    private const char RuleSeparator = '\u001E';

    /// <summary>Separates the values inside a field holding several: the actions and the triggers.</summary>
    /// <remarks>
    /// A third separator rather than a reused one, so that two actions cannot render as the same text as one action whose
    /// next field begins where the second would have. Every multi-valued field uses this one, which stays unambiguous
    /// because a field separator ends each of them.
    /// </remarks>
    private const char ListSeparator = '\u001D';

    private readonly string? value;

    private MailRuleSetRevision(string value) => this.value = value;

    /// <summary>Gets whether this value names a revision rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the identity, as lowercase hexadecimal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a revision.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a revision.");

    /// <summary>Reports whether a value carries a character this identity separates its own fields with.</summary>
    /// <param name="value">A condition or a destination folder about to become part of a rule set.</param>
    /// <returns><see langword="true" /> when the value would blur the boundary between two fields of the digest input.</returns>
    /// <remarks>
    /// The remarks above say a separator cannot occur in what is hashed, and this is what lets that be true rather than
    /// hoped for: a write validating a candidate set asks first and refuses a value that answers yes. Without the check
    /// the claim rests on the expression language's grammar, which is somebody else's decision and can change under this
    /// one — and a rule set that renders identically to a different rule set is exactly the collision this identity
    /// exists to make impossible.
    /// </remarks>
    public static bool ContainsSeparator(string? value) =>
        value?.AsSpan().IndexOfAny(FieldSeparator, RuleSeparator, ListSeparator) >= 0;

    /// <summary>Derives the identity of one account's rule set from its rules, in the order they are evaluated.</summary>
    /// <param name="declarations">Every rule of the set in position order, disabled ones included; they are left out here.</param>
    /// <returns>The revision the set is known by.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A set that enables no rule has an identity too, and it is the same one for every account that enables none. That
    /// is deliberate: an account whose last rule was removed is in a state a history should be able to name, rather than
    /// one with no revision at all.
    /// </remarks>
    public static MailRuleSetRevision Create(IReadOnlyList<MailRuleDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var canonicalForm = new StringBuilder();

        foreach (var declaration in declarations.Where(declaration => declaration.Enabled))
        {
            canonicalForm
                .Append(declaration.Id)
                .Append(FieldSeparator)
                .Append(declaration.ConditionText)
                .Append(FieldSeparator)
                .AppendJoin(ListSeparator, declaration.Actions.Select(action => action.CanonicalForm))
                .Append(FieldSeparator)
                .Append(declaration.StopWhenMatched ? "stop" : "continue")
                .Append(FieldSeparator)
                .AppendJoin(ListSeparator, declaration.Triggers.Select(trigger => trigger.Name))
                .Append(FieldSeparator)
                .Append(declaration.Schedule?.CanonicalForm ?? string.Empty)
                .Append(RuleSeparator);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalForm.ToString()));

        return new MailRuleSetRevision(Convert.ToHexStringLower(digest)[..LengthInCharacters]);
    }

    /// <summary>Reads back an identity this system derived earlier and recorded.</summary>
    /// <param name="value">The recorded identity.</param>
    /// <returns>The revision, which compares equal to a freshly derived one of the same rule set.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not an identity this type could have produced.</exception>
    /// <remarks>
    /// A durable record of a run has to say which rule set the run was bound to, and it survives the process that
    /// derived it — so the identity has to come back from storage rather than only out of <see cref="Create" />. The
    /// shape is checked rather than trusted, because a value that is not one this type produces would compare unequal
    /// to every rule set and silently make every run look superseded.
    /// </remarks>
    public static MailRuleSetRevision Restore(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != LengthInCharacters
            || !value.All(static character => char.IsAsciiDigit(character) || char.IsAsciiLetterLower(character) && character <= 'f'))
        {
            throw new ArgumentException(
                $"A rule set revision is exactly {LengthInCharacters} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new MailRuleSetRevision(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";
}
