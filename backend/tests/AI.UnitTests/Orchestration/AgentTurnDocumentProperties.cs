// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml;
using System.Xml.Linq;
using CsCheck;
using MailFathom.AI.Orchestration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>States what the writer's cut has to be true of for every text and every budget, not for the ones written down.</summary>
/// <remarks>
/// The budget is drawn from above the smallest document the structure here can be written in, which is the
/// precondition the writer states: closing tags are always written, so a budget smaller than the structure is exceeded
/// by the structure alone. Lone surrogates are left out of the text because the writer replaces them, which an example
/// covers, and so is a carriage return, because the parser reading the document back normalizes it where the writer
/// left it as written; everything else a sender could write is in, including the characters that would end an element.
/// </remarks>
public sealed class AgentTurnDocumentProperties
{
    /// <summary>How many inputs each property here draws.</summary>
    private const int Iterations = 300;

    private static readonly Gen<string> Fragments = Gen.OneOf(
        Gen.String[Gen.Char['a', 'z'], 1, 12],
        Gen.OneOfConst("<", ">", "&", "\"", "'", "</extract>", "</message>", "<ask>", "]]>", "&amp;"),
        Gen.OneOfConst("\n", "\t", " ", "\u0001", "\uFFFE"),
        Gen.OneOfConst("😀", "👩‍👩‍👧‍👦", "🇵🇱", "é", "中"));

    private static readonly Gen<(string Text, int Budget, bool AlreadyCut)> Cases =
        from fragments in Fragments.Array[0, 300]
        from budget in Gen.Int[120, 2_000]
        from alreadyCut in Gen.Bool
        select (string.Concat(fragments), budget, alreadyCut);

    /// <summary>Whatever a sender wrote, and whether or not its caller had cut it already, the document stays inside its budget, parses, and quotes a prefix of the text it was given.</summary>
    [Fact]
    public void WriteQuoted_AnyTextAndAnyBudget_StaysWithinTheBudgetAndQuotesAPrefixOfTheText()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                using var document = new AgentTurnDocument(input.Budget);

                document.StartElement("message");
                document.WriteAttribute("id", "1");
                document.WriteQuoted("extract", input.Text, input.AlreadyCut);

                var output = document.Complete();
                var extract = Lenient(output).Descendants("extract").Single();

                Assert.True(output.Length <= input.Budget, $"{output.Length} characters were written for a budget of {input.Budget}.");
                Assert.StartsWith(extract.Value, input.Text, StringComparison.Ordinal);
                Assert.Equal(
                    input.AlreadyCut || extract.Value.Length < input.Text.Length,
                    extract.Attribute(AgentTurnDocument.CutAttributeName) is not null);
            },
            Iterations);
    }

    /// <summary>Reads a document the way a model is shown it, accepting the references the writer writes control characters as.</summary>
    private static XElement Lenient(string document)
    {
        using var reader = XmlReader.Create(
            new StringReader($"<turn>{document}</turn>"),
            new XmlReaderSettings { CheckCharacters = false });

        return XElement.Load(reader, LoadOptions.PreserveWhitespace);
    }
}
