using System.Text;
using System.Text.Encodings.Web;

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
    public void Visit(TextNode node) => _result.Append(HtmlEncoder.Default.Encode(node.Value));

    /// <inheritdoc />
    public void Visit(RubyNode node)
    {
        _result.Append("<ruby>")
            .Append(HtmlEncoder.Default.Encode(node.BaseText))
            .Append("<rt>")
            .Append(HtmlEncoder.Default.Encode(node.GlossText))
            .Append("</rt></ruby>");
    }

    /// <inheritdoc />
    public void Visit(UnresolvedGlossNode node) => _result.Append(HtmlEncoder.Default.Encode(node.BaseText));

    /// <inheritdoc />
    public void Visit(PronunciationNode node)
    {
        _result.Append("<span class=\"phonetic-annotation\" lang=\"")
            .Append(HtmlEncoder.Default.Encode(node.Language.Value))
            .Append("\"><span class=\"base\">")
            .Append(HtmlEncoder.Default.Encode(node.BaseText))
            .Append("</span><span class=\"reading\">")
            .Append(HtmlEncoder.Default.Encode(node.Pronunciation))
            .Append("</span></span>");
    }

    /// <inheritdoc />
    public string GetResult() => _result.ToString();
}