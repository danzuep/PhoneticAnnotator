using System.Runtime.CompilerServices;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Converts a token stream into an owned AST document.
/// </summary>
public static class GlossDocumentAssembler
{
    /// <summary>
    /// Disambiguates tokens and copies their text into an immutable document tree.
    /// </summary>
    /// <param name="tokens">The source-backed token stream.</param>
    /// <param name="disambiguator">The reading disambiguator.</param>
    /// <param name="cancellationToken">A token used to cancel assembly.</param>
    /// <returns>An AST preserving token order and unresolved candidates.</returns>
    public static async Task<DocumentNode> AssembleAsync(
        IAsyncEnumerable<MemoryToken> tokens,
        IPhoneticDisambiguator disambiguator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(disambiguator);

        var children = new List<GlossAstNode>();
        await foreach (var token in disambiguator
            .DisambiguateStreamAsync(tokens, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var baseText = token.SourceToken.Text.ToString();
            switch (token.Status)
            {
                case ReadingResolution.Resolved:
                    children.Add(new RubyNode(
                        baseText,
                        token.ResolvedReading!,
                        token.SourceToken.Language,
                        token.SourceToken.ReadingSystem));
                    break;
                case ReadingResolution.Ambiguous:
                    children.Add(new UnresolvedGlossNode(
                        baseText,
                        token.SourceToken.ReadingCandidates,
                        token.SourceToken.Language,
                        token.SourceToken.ReadingSystem));
                    break;
                default:
                    children.Add(new TextNode(baseText));
                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DocumentNode(children.AsReadOnly());
    }
}