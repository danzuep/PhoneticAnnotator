using System.Text;
#if !NETWASM
using System.Text.Encodings.Web;
#endif

namespace PhoneticAnnotator.Core;

/// <summary>
/// Renders gloss documents as HTML ruby with encoded text content.
/// </summary>
public sealed class HtmlRubyVisitor : IGlossAstVisitor
{
    private readonly StringBuilder _result = new();

    /// <inheritdoc />
    public void Visit(DocumentNode node)
    {
        foreach (var child in node.Children)
        {
            child.Accept(this);
        }
    }

    /// <inheritdoc />
    public void Visit(TextNode node) => _result.Append(Encode(node.Value));

    /// <inheritdoc />
    public void Visit(RubyNode node)
    {
        _result.Append("<ruby>")
            .Append(Encode(node.BaseText))
            .Append("<rt>")
            .Append(Encode(node.GlossText))
            .Append("</rt></ruby>");
    }

    /// <inheritdoc />
    public void Visit(UnresolvedGlossNode node) => _result.Append(Encode(node.BaseText));

    /// <inheritdoc />
    public void Visit(PronunciationNode node)
    {
        _result.Append("<span class=\"phonetic-annotation\" lang=\"")
            .Append(Encode(node.Language.Value))
            .Append("\"><span class=\"base\">")
            .Append(Encode(node.BaseText))
            .Append("</span><span class=\"reading\">")
            .Append(Encode(node.Pronunciation))
            .Append("</span></span>");
    }

    /// <inheritdoc />
    public string GetResult() => _result.ToString();

    private static string Encode(string value)
    {
#if NETWASM
        // NetWasm's encoding package exposes JavaScriptEncoder, but not HtmlEncoder.
        var encoded = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&': encoded.Append("&amp;"); break;
                case '<': encoded.Append("&lt;"); break;
                case '>': encoded.Append("&gt;"); break;
                case '"': encoded.Append("&quot;"); break;
                case '\'': encoded.Append("&#39;"); break;
                default: encoded.Append(character); break;
            }
        }
        return encoded.ToString();
#else
        return HtmlEncoder.Default.Encode(value);
#endif
    }
}