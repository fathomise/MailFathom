// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Reflection;
using PdfSharp.Fonts;

namespace MailFathom.Infrastructure.Documents.Reports;

/// <summary>The two typefaces a report is set in, served to PDFsharp from the files embedded in this assembly.</summary>
/// <remarks>
/// <para>
/// <strong>The body is Source Serif 4</strong> — paragraphs, lists, and quotations, with a true italic and a semibold for
/// emphasis — and <strong>everything around it is Geist</strong>: the title block, headings, tables, the running foot,
/// and the sources. Both are under the SIL Open Font License 1.1, which permits embedding a subset in a document handed
/// out, and both carry every letter Polish needs.
/// </para>
/// <para>
/// Geist publishes no italic, so an emphasis set in Geist — inside a table — takes the medium weight instead, as the
/// design draws it, and a strong one the semibold. Nothing is simulated: a slanted or emboldened glyph PDFsharp made up
/// would not be the typeface.
/// </para>
/// <para>
/// <strong>PDFsharp holds one resolver for the whole process</strong> and refuses a second once a font was used, so this
/// one is installed once, on the first report, and answers every family a report names. A family it does not know falls
/// to the body face rather than to whatever the host has installed, so no report depends on the machine it is set on.
/// </para>
/// </remarks>
internal sealed class ReportFonts : IFontResolver
{
    /// <summary>The family the body is set in.</summary>
    internal const string Serif = "Source Serif 4";

    /// <summary>The family headings, tables, and the frame are set in.</summary>
    internal const string Sans = "Geist";

    private const string ResourcePrefix = "MailFathom.Infrastructure.Documents.Reports.Fonts.";

    private static readonly Lock InstallLock = new();

    private ReportFonts()
    {
    }

    /// <summary>Installs the resolver into PDFsharp, once for the process.</summary>
    /// <exception cref="InvalidOperationException">Thrown when something else already installed a resolver of its own.</exception>
    internal static void Install()
    {
        lock (InstallLock)
        {
            if (GlobalFontSettings.FontResolver is ReportFonts)
            {
                return;
            }

            if (GlobalFontSettings.FontResolver is not null)
            {
                throw new InvalidOperationException("Another font resolver is installed in PDFsharp; a report is set in its own typefaces only.");
            }

            GlobalFontSettings.FontResolver = new ReportFonts();
        }
    }

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = string.Equals(familyName, Sans, StringComparison.OrdinalIgnoreCase)
            ? (isBold, isItalic) switch
            {
                (true, _) => "Geist-SemiBold",
                (false, true) => "Geist-Medium",
                _ => "Geist-Regular",
            }
            : (isBold, isItalic) switch
            {
                (true, true) => "SourceSerif4-SemiboldIt",
                (true, false) => "SourceSerif4-Semibold",
                (false, true) => "SourceSerif4-It",
                _ => "SourceSerif4-Regular",
            };

        return new FontResolverInfo(face);
    }

    /// <inheritdoc />
    /// <remarks>PDFsharp asks once per face and keeps what it read, so a face is read out of the assembly once for the process.</remarks>
    public byte[]? GetFont(string faceName)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ResourcePrefix}{faceName}.ttf");

        if (resource is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);

        // PDFsharp's own contract takes the face as an array.
        return buffer.ToArray();
    }
}
