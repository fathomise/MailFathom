// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Infrastructure.Persistence.Footers;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Footers;

public sealed class MailFooterDocumentTests
{
    [Fact]
    public void Parse_ARenderedFooter_ReadsBackTheMarkupAndEveryImageByte()
    {
        // Arrange
        byte[] logo = [137, 80, 78, 71, 13, 10, 26, 10];
        var footer = new MailFooterContent("<p>{{displayName}}</p><img src=\"cid:logo.png\">", [new MailFooterImage("logo.png", "image/png", logo)]);

        // Act
        var parsed = MailFooterDocument.Parse(MailFooterDocument.Render(footer));

        // Assert
        Assert.Equal(footer.Html, parsed.Html);
        var image = Assert.Single(parsed.Images);
        Assert.Equal(("logo.png", "image/png"), (image.Name, image.MediaType));
        Assert.Equal(logo, image.Content.ToArray());
    }

    [Fact]
    public void Parse_ANullDocument_IsRefused()
    {
        // Act
        var parse = () => MailFooterDocument.Parse("null");

        // Assert
        Assert.Throws<InvalidOperationException>(parse);
    }
}
