using System.Text.Json;
using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Serializes gloss AST nodes to a compact, discriminated JSON representation.
/// </summary>
public sealed class JsonAstVisitor : IGlossAstVisitor
{
    private string _result = string.Empty;

    /// <inheritdoc />
    public void Visit(DocumentNode node) => SetResult(node);

    /// <inheritdoc />
    public void Visit(TextNode node) => SetResult(node);

    /// <inheritdoc />
    public void Visit(RubyNode node) => SetResult(node);

    /// <inheritdoc />
    public void Visit(UnresolvedGlossNode node) => SetResult(node);

    /// <inheritdoc />
    public void Visit(PronunciationNode node) => SetResult(node);

    /// <inheritdoc />
    public string GetResult() => _result;

    private void SetResult(GlossAstNode node) =>
        _result = JsonSerializer.Serialize(CreateJsonModel(node));

    private static object CreateJsonModel(GlossAstNode node) => node switch
    {
        DocumentNode document => new
        {
            type = "document",
            children = document.Children.Select(CreateJsonModel).ToArray()
        },
        TextNode text => new
        {
            type = "text",
            value = text.Value
        },
        RubyNode ruby => new
        {
            type = "ruby",
            baseText = ruby.BaseText,
            glossText = ruby.GlossText,
            language = ruby.Language.Value,
            readingSystem = CreateReadingSystemModel(ruby.ReadingSystem)
        },
        UnresolvedGlossNode unresolved => new
        {
            type = "unresolved",
            baseText = unresolved.BaseText,
            readingCandidates = unresolved.ReadingCandidates,
            language = unresolved.Language.Value,
            readingSystem = CreateReadingSystemModel(unresolved.ReadingSystem)
        },
        PronunciationNode pronunciation => new
        {
            type = "pronunciation",
            baseText = pronunciation.BaseText,
            pronunciation = pronunciation.Pronunciation,
            language = pronunciation.Language.Value,
            readingSystem = CreateReadingSystemModel(pronunciation.ReadingSystem)
        },
        _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unsupported AST node type.")
    };

    private static object CreateReadingSystemModel(ReadingSystem readingSystem) => new
    {
        tag = readingSystem.Tag,
        name = readingSystem.Name,
        sourceField = readingSystem.SourceField.ToString()
    };
}