// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>One phone number a caller states for a contact, as the caller wrote it and before any rule has judged it.</summary>
/// <param name="Number">The number as typed.</param>
/// <param name="Label">The name of one <see cref="ContactPhoneLabel" />, compared without regard to case.</param>
/// <remarks>The number is personal data about a third party: nothing logs a draft, and no refusal repeats it.</remarks>
public sealed record ContactPhoneNumberDraft(string? Number, string? Label)
{
    /// <summary>Reads the phone numbers a record states, in the order the caller wrote them.</summary>
    /// <param name="drafts">The numbers stated, or <see langword="null" /> when the caller stated none at all.</param>
    /// <returns>The validated numbers, or <see langword="null" /> when <paramref name="drafts" /> is.</returns>
    /// <exception cref="ContactRecordInvalidException">Thrown naming the one rule a number or the list broke.</exception>
    /// <remarks>
    /// Shared by every surface that writes a record, so the administrative routes and the signed-in user's are refused
    /// for the same reasons in the same words. An absent list stays absent rather than becoming an empty one, because an
    /// amendment reads absence as keeping the numbers held.
    /// </remarks>
    public static IReadOnlyList<ContactPhoneNumber>? ReadAll(IReadOnlyList<ContactPhoneNumberDraft>? drafts)
    {
        if (drafts is null)
        {
            return null;
        }

        if (drafts.Count > Contact.MaximumPhoneNumberCount)
        {
            throw ContactRecordInvalidException.TooManyPhoneNumbers();
        }

        return [.. drafts.Select(Read)];
    }

    /// <summary>Reads one number, refusing a malformed number or an unknown label.</summary>
    private static ContactPhoneNumber Read(ContactPhoneNumberDraft? draft)
    {
        var trimmed = draft?.Number?.Trim() ?? string.Empty;

        if (!ContactPhoneNumber.IsWellFormed(trimmed))
        {
            throw ContactRecordInvalidException.NotAPhoneNumber();
        }

        var label = Enum.GetNames<ContactPhoneLabel>()
            .FirstOrDefault(name => string.Equals(name, draft?.Label?.Trim(), StringComparison.OrdinalIgnoreCase));

        return label is null
            ? throw ContactRecordInvalidException.NotAPhoneLabel()
            : ContactPhoneNumber.Create(trimmed, Enum.Parse<ContactPhoneLabel>(label));
    }
}
