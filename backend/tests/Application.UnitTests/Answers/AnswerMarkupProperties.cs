// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using CsCheck;
using MailFathom.Application.Answers;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Answers;

/// <summary>States what neutralising an answer's pictures holds for every text, rather than for the shapes written down beside it.</summary>
/// <remarks>
/// The text is drawn from the characters picture and tag markup is made of, so the generator reaches the runs of marks,
/// brackets and backslashes the examples cannot enumerate. A stored answer is neutralised again each time it is read, so
/// a text the rule changed on a second pass would gain a character on every read of the run that holds it. A tag opener is
/// a bracket before a name that ends where a parser ends one — at a space, a slash or a closing bracket — so a bracket
/// before a name followed by anything else opens nothing and is left as written.
/// </remarks>
public sealed class AnswerMarkupProperties
{
    /// <summary>How many texts each property here draws.</summary>
    private const int Iterations = 500;

    private const int LongestText = 80;

    private static readonly Gen<string> MarkupTexts = Gen.String[
        Gen.OneOfConst('!', '[', ']', '(', ')', '<', '>', '\\', '/', '?', '-', ':', ' ', '\n', 'a', 'I', 'm', 'g'),
        0,
        LongestText];

    /// <summary>No text leaves the rule carrying a picture or tag opener, and nothing the rule would still change.</summary>
    [Fact]
    public void WithoutPictures_AnyText_LeavesNoPictureAndNothingASecondPassWouldChange()
    {
        // Act, Assert
        PropertyCheck.Holds(
            MarkupTexts,
            text =>
            {
                var published = AnswerMarkup.WithoutPictures(text);

                Assert.DoesNotContain("![", published, StringComparison.Ordinal);
                Assert.DoesNotMatch(@"<(?:/?[A-Za-z][A-Za-z0-9-]*(?:[\s/>]|$)|[!?])", published);
                Assert.Equal(published, AnswerMarkup.WithoutPictures(published));
            },
            Iterations);
    }
}
