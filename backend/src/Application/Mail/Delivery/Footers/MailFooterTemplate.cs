// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text.RegularExpressions;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>The variables an organization's footer template names, and how a member's values are filled into it.</summary>
/// <remarks>
/// <para>
/// A variable is written <c>{{name}}</c>, and the template names nothing else: a variable the template names and this
/// does not fill is refused when the template is saved, so a misspelt one never reaches a member's message as literal
/// braces.
/// </para>
/// <para>
/// A value is encoded for markup when it is filled in. The values are what an administrator and a member wrote about
/// the member, and a name with an angle bracket in it is still a name rather than markup in everybody's footer.
/// </para>
/// </remarks>
public static partial class MailFooterTemplate
{
    /// <summary>The variable filled with the member's display name.</summary>
    public const string DisplayNameVariable = "displayName";

    /// <summary>The variable filled with the member's job title, or with nothing where they have none.</summary>
    public const string JobTitleVariable = "jobTitle";

    /// <summary>The variable filled with the member's preferred phone number, or with nothing where they have none.</summary>
    public const string PhoneNumberVariable = "phoneNumber";

    /// <summary>Gets every variable a template may name.</summary>
    public static IReadOnlyList<string> Variables { get; } = [DisplayNameVariable, JobTitleVariable, PhoneNumberVariable];

    /// <summary>Finds the first variable a template names that is not one of <see cref="Variables" />.</summary>
    /// <param name="html">The template's markup.</param>
    /// <returns>The variable's name as the template wrote it, or <see langword="null" /> when every one is known.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="html" /> is <see langword="null" />.</exception>
    public static string? FindUnknownVariable(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return VariablePattern().Matches(html)
            .Select(static match => match.Groups["name"].Value)
            .FirstOrDefault(static name => !Variables.Contains(name, StringComparer.Ordinal));
    }

    /// <summary>Fills a template's variables with one member's values, each encoded for markup.</summary>
    /// <param name="html">The template's markup.</param>
    /// <param name="sender">The member's values.</param>
    /// <returns>The markup the member's message carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static string Fill(string html, MailFooterSender sender)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(sender);

        return VariablePattern().Replace(html, match => WebUtility.HtmlEncode(match.Groups["name"].Value switch
        {
            DisplayNameVariable => sender.DisplayName,
            JobTitleVariable => sender.JobTitle,
            PhoneNumberVariable => sender.PhoneNumber,
            _ => null,
        } ?? string.Empty));
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[^{}]*?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
