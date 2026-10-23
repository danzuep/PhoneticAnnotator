using System.Buffers;
using System.Text;
using System.Text.Json;

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

    private void SetResult(GlossAstNode node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteNode(writer, node);
        }
        _result = Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteNode(Utf8JsonWriter writer, GlossAstNode node)
    {
        writer.WriteStartObject();
        switch (node)
        {
            case DocumentNode document:
                writer.WriteString("type", "document");
                writer.WriteStartArray("children");
                foreach (var child in document.Children)
                {
                    WriteNode(writer, child);
                }
                writer.WriteEndArray();
                break;
            case TextNode text:
                writer.WriteString("type", "text");
                writer.WriteString("value", text.Value);
                break;
            case RubyNode ruby:
                writer.WriteString("type", "ruby");
                writer.WriteString("baseText", ruby.BaseText);
                writer.WriteString("glossText", ruby.GlossText);
                WriteMetadata(writer, ruby.Language, ruby.ReadingSystem);
                break;
            case UnresolvedGlossNode unresolved:
                writer.WriteString("type", "unresolved");
                writer.WriteString("baseText", unresolved.BaseText);
                writer.WriteStartArray("readingCandidates");
                foreach (var candidate in unresolved.ReadingCandidates)
                {
                    writer.WriteStringValue(candidate);
                }
                writer.WriteEndArray();
                WriteMetadata(writer, unresolved.Language, unresolved.ReadingSystem);
                break;
            case PronunciationNode pronunciation:
                writer.WriteString("type", "pronunciation");
                writer.WriteString("baseText", pronunciation.BaseText);
                writer.WriteString("pronunciation", pronunciation.Pronunciation);
                WriteMetadata(writer, pronunciation.Language, pronunciation.ReadingSystem);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unsupported AST node type.");
        }
        writer.WriteEndObject();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, LanguageCode language, ReadingSystem readingSystem)
    {
        writer.WriteString("language", language.Value);
        writer.WriteStartObject("readingSystem");
        writer.WriteString("tag", readingSystem.Tag);
        writer.WriteString("name", readingSystem.Name);
        writer.WriteString("sourceField", readingSystem.SourceField.ToString());
        writer.WriteString("annotationKind", readingSystem.AnnotationKind.ToString());
        writer.WriteEndObject();
    }
}