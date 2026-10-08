namespace PhoneticAnnotator.Core;

/// <summary>
/// A node in an immutable phonetic gloss document.
/// </summary>
public abstract record GlossAstNode
{
    /// <summary>
    /// Dispatches this node to a visitor.
    /// </summary>
    /// <param name="visitor">The visitor to receive this node.</param>
    public abstract void Accept(IGlossAstVisitor visitor);
}

/// <summary>
/// A document containing ordered gloss nodes.
/// </summary>
public sealed record DocumentNode(IReadOnlyList<GlossAstNode> Children) : GlossAstNode
{
    /// <inheritdoc />
    public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Unannotated source text.
/// </summary>
public sealed record TextNode(string Value) : GlossAstNode
{
    /// <inheritdoc />
    public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Source text paired with a resolved CJK reading.
/// </summary>
public sealed record RubyNode(
    string BaseText,
    string GlossText,
    LanguageCode Language,
    ReadingSystem ReadingSystem) : GlossAstNode
{
    /// <inheritdoc />
    public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Source text whose reading candidates were not resolved.
/// </summary>
public sealed record UnresolvedGlossNode(
    string BaseText,
    IReadOnlyList<string> ReadingCandidates,
    LanguageCode Language,
    ReadingSystem ReadingSystem) : GlossAstNode
{
    /// <inheritdoc />
    public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Source text paired with Arabic Tashkeel or another non-ruby annotation.
/// </summary>
public sealed record PronunciationNode(
    string BaseText,
    string Pronunciation,
    LanguageCode Language,
    ReadingSystem ReadingSystem) : GlossAstNode
{
    /// <inheritdoc />
    public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Visits every supported gloss AST node.
/// </summary>
public interface IGlossAstVisitor
{
    /// <summary>Visits a document node.</summary>
    void Visit(DocumentNode node);

    /// <summary>Visits plain text.</summary>
    void Visit(TextNode node);

    /// <summary>Visits a resolved ruby annotation.</summary>
    void Visit(RubyNode node);

    /// <summary>Visits a node with unresolved candidates.</summary>
    void Visit(UnresolvedGlossNode node);

    /// <summary>Visits a paired pronunciation annotation.</summary>
    void Visit(PronunciationNode node);

    /// <summary>Gets the current rendered result.</summary>
    string GetResult();
}