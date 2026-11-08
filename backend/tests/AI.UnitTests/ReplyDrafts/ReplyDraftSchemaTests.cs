// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.ReplyDrafts;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers the schema a drafting asks a model for: that its numbers are the turn's own, and that what it admits reads into a draft.</summary>
public sealed class ReplyDraftSchemaTests
{
    private const string Target = "https://files.example.com/sheet?token=secret";

    /// <summary>
    /// An answer written to the schema built for <see cref="Turn" />: every object carrying every member, a nullable one
    /// written as null, a run carrying its link, a list block, and a table with a cell carrying an alignment.
    /// </summary>
    private const string Conforming = """
        {
          "body": [
            "Thank you, the quotation works for us.",
            {"type": "paragraph", "runs": ["The figures are in ", {"text": "the comparison sheet", "bold": false, "italic": false, "colour": null, "link": 0}, "."], "align": null},
            {"type": "list", "ordered": true, "items": ["Confirm the delivery date", [{"text": "Send the invoice", "bold": true, "italic": false, "colour": null, "link": null}]]},
            {"type": "table", "header": true, "rows": [["Item", "Price"], ["Racking", {"runs": ["4 200"], "align": "end"}]]}
          ],
          "claims": [{"text": "The price is 4 200 zloty.", "messages": [1]}],
          "recipients": [1]
        }
        """;

    private static readonly StoredEmailId First = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Second = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public void For_ATurn_LimitsEveryMessagePersonAndLinkNumberToTheOnesItPublished()
    {
        // Act
        var schema = ReplyDraftSchema.For(Turn()).Schema!.Value;

        // Assert
        var properties = schema.GetProperty("properties");
        var claimMessages = properties.GetProperty("claims").GetProperty("items").GetProperty("properties").GetProperty("messages").GetProperty("items");
        var runLink = schema.GetProperty("$defs").GetProperty("run").GetProperty("anyOf")[1].GetProperty("properties").GetProperty("link").GetProperty("anyOf")[0];

        Assert.Equal([0, 1], NumbersIn(claimMessages));
        Assert.Equal([0, 1], NumbersIn(properties.GetProperty("recipients").GetProperty("items")));
        Assert.Equal([0], NumbersIn(runLink));
    }

    /// <summary>An enum of no values is a schema no provider accepts, so a member the turn published nothing for is left out instead.</summary>
    [Fact]
    public void For_ATurnPublishingNoMessagesPeopleOrLinks_LeavesThoseMembersOut()
    {
        // Act
        var schema = ReplyDraftSchema.For(new ReplyDraftTurn("a turn", [], [], [])).Schema!.Value;

        // Assert
        var properties = schema.GetProperty("properties");
        var run = schema.GetProperty("$defs").GetProperty("run").GetProperty("anyOf")[1].GetProperty("properties");

        Assert.Equal(["body"], properties.EnumerateObject().Select(static property => property.Name));
        Assert.False(run.TryGetProperty("link", out _));
    }

    /// <summary>Strict mode refuses an object that leaves a member optional or admits another, so every object states both.</summary>
    [Fact]
    public void For_ATurn_RequiresEveryMemberOfEveryObjectAndAdmitsNoOther()
    {
        // Act
        var schema = ReplyDraftSchema.For(Turn()).Schema!.Value;

        // Assert
        var objects = ObjectsIn(schema).ToList();

        Assert.NotEmpty(objects);
        Assert.All(objects, static node =>
        {
            Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(
                node.GetProperty("properties").EnumerateObject().Select(static property => property.Name),
                node.GetProperty("required").EnumerateArray().Select(static name => name.GetString()));
        });
    }

    [Fact]
    public void Read_AnAnswerTheSchemaAdmits_ReadsIntoADraftCarryingItsLinkItsListAndItsTable()
    {
        // Act
        var draft = ReplyDraftReading.Read(Conforming, Messages(), Participants(), Links());

        // Assert
        var blocks = draft.Document.Blocks;
        var linked = Assert.IsType<MailParagraphBlock>(blocks[1]).Content.Single(static run => run.Link is not null);
        var list = Assert.IsType<MailListBlock>(blocks[2]);
        var table = Assert.IsType<MailTableBlock>(blocks[3]);

        Assert.True(draft.WasWritten);
        Assert.Equal("the comparison sheet", linked.Text);
        Assert.True(list.Ordered);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(MailBlockAlignment.End, table.Rows[1].Cells[1].Alignment);
        Assert.Equal([Second], Assert.Single(draft.Claims).Sources);
        Assert.Equal("karolina@example.test", Assert.Single(draft.ProposedRecipients).Address);
    }

    private static ReplyDraftTurn Turn() => new("a turn", Links(), [0, 1], [0, 1]);

    private static int[] NumbersIn(JsonElement schema) =>
        [.. schema.GetProperty("enum").EnumerateArray().Select(static number => number.GetInt32())];

    /// <summary>Every object schema anywhere in the document, found by its declared type.</summary>
    private static IEnumerable<JsonElement> ObjectsIn(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => (node.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String && type.GetString() == "object" ? [node] : Array.Empty<JsonElement>())
            .Concat(node.EnumerateObject().SelectMany(static property => ObjectsIn(property.Value))),
        JsonValueKind.Array => node.EnumerateArray().SelectMany(ObjectsIn),
        _ => [],
    };

    private static IReadOnlyList<ReplyDraftLink> Links() =>
        [new ReplyDraftLink(0, MailLinkReader.Read(Target, "comparison sheet")!, "comparison sheet")];

    private static IReadOnlyList<ReplyDraftMessage> Messages() =>
    [
        new(First, 0, "Anna", new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), "Could you quote for the batch?", IsChosen: false, Attachments: []),
        new(Second, 1, "Karolina", new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero), "It is 4 200 zloty.", IsChosen: true, Attachments: []),
    ];

    private static IReadOnlyList<ReplyDraftParticipant> Participants()
    {
        EmailAddress.TryCreate("Anna", "anna@example.test", out var anna);
        EmailAddress.TryCreate("Karolina", "karolina@example.test", out var karolina);

        return [new ReplyDraftParticipant(0, anna), new ReplyDraftParticipant(1, karolina)];
    }
}
