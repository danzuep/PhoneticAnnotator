using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Renders gloss documents using Anki-style <c>base[reading]</c> syntax.
/// </summary>
public sealed class AnkiSyntaxVisitor : IGlossAstVisitor
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
    public void Visit(TextNode node) => _result.Append(Escape(node.Value));

    /// <inheritdoc />
    public void Visit(RubyNode node) => AppendAnnotation(node.BaseText, node.GlossText);

    /// <inheritdoc />
    public void Visit(UnresolvedGlossNode node) => _result.Append(Escape(node.BaseText));

    /// <inheritdoc />
    public void Visit(PronunciationNode node) => AppendAnnotation(node.BaseText, node.Pronunciation);

    /// <inheritdoc />
    public string GetResult() => _result.ToString();

    private void AppendAnnotation(string baseText, string reading) =>
        _result.Append(Escape(baseText)).Append('[').Append(Escape(reading)).Append(']');

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal);
}