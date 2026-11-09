// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.AI.Orchestration;

/// <summary>States, on a run's turn, the languages the mailboxes it may search are kept in.</summary>
/// <remarks>
/// <para>
/// A lookup matches words rather than meaning, so mail written in Polish is reached by Polish words and by nothing an
/// English question carries. A model told nothing searches in the question's language and concludes the mail is not
/// there; told which languages the mailboxes in reach are kept in, it can try each before concluding.
/// </para>
/// <para>
/// The languages are each account's own, read through <see cref="IMailAccountLanguages" />: the language this
/// deployment writes about that mailbox in, which its operator set to the language its mail is in. They ride on the turn
/// rather than in the instruction, because they change per run and the instruction is recorded as the digest of a text
/// that does not.
/// </para>
/// </remarks>
internal static class MailboxLanguages
{
    /// <summary>Reads the languages a set of accounts is kept in, each named once.</summary>
    /// <param name="accounts">The accounts the run may search.</param>
    /// <param name="languages">Answers each account's language.</param>
    /// <returns>The languages, each once, in the order the enumeration declares them, or none where no account is in reach.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static IReadOnlyList<MailAccountLanguage> Of(IEnumerable<MailAccountId> accounts, IMailAccountLanguages languages)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(languages);

        return [.. accounts.Select(languages.LanguageOf).Distinct().Order()];
    }

    /// <summary>Writes the sentence a turn names the languages in, or nothing where there are none.</summary>
    /// <param name="languages">The languages, each once.</param>
    /// <returns>The sentence, ending in a line break, or the empty string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="languages" /> is <see langword="null" />.</exception>
    /// <remarks>Each member's name is the language's own name in English, which is what <see cref="MailAccountLanguage" /> promises.</remarks>
    internal static string Stated(IReadOnlyList<MailAccountLanguage> languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        return languages.Count is 0
            ? string.Empty
            : $"The mailboxes you may search are kept in: {string.Join(", ", languages)}.\n";
    }
}
