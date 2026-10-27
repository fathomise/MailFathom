// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Signatures;

/// <summary>
/// The text a signer carries comes from whoever made the certificate, before anything trusts it, so it is made safe to
/// store: a value PostgreSQL refuses would be a message that is never stored.
/// </summary>
public sealed class EmailSignerTests
{
    [Fact]
    public void Name_LongerThanTheBoundWithAPairAcrossTheCut_KeepsNoHalfOfThePair()
    {
        // Arrange
        var name = new string('a', EmailSigner.MaximumNameLength - 1) + "\U0001F600" + "tail";

        // Act
        var signer = new EmailSigner { Name = name };

        // Assert
        Assert.Equal(new string('a', EmailSigner.MaximumNameLength - 1), signer.Name);
    }

    [Fact]
    public void Name_CarryingControlAndFormattingCharacters_KeepsOnlyThePrintableText()
    {
        // Act
        var signer = new EmailSigner { Name = "Anna\u0000 Kowalska‮" };

        // Assert
        Assert.Equal("Anna Kowalska", signer.Name);
    }

    [Fact]
    public void Issuer_CarryingAnUnpairedSurrogate_ReplacesItWithTheReplacementCharacter()
    {
        // Act
        var signer = new EmailSigner { Issuer = "\uD800Root" };

        // Assert
        Assert.Equal("�Root", signer.Issuer);
    }

    [Fact]
    public void Name_OnlyUnprintableCharacters_IsAbsent()
    {
        // Act
        var signer = new EmailSigner { Name = "\u0000‎" };

        // Assert
        Assert.Null(signer.Name);
    }

    [Fact]
    public void ChainEnd_CarryingAControlCharacterAndOverTheBound_IsBoundedLikeASignerName()
    {
        // Arrange
        var chainEnd = "Root\u0007" + new string('r', EmailSigner.MaximumNameLength);

        // Act
        var signature = new EmailSignature { ChainEnd = chainEnd };

        // Assert
        Assert.Equal("Root" + new string('r', EmailSigner.MaximumNameLength - 4), signature.ChainEnd);
    }
}
