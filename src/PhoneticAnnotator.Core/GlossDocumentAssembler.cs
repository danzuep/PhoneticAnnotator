using System.Buffers;
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
        => await AssembleAsync(tokens, disambiguator, null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Disambiguates tokens, optionally aligns resolved readings, and copies the results into an owned AST.
    /// </summary>
    /// <param name="tokens">The source-backed token stream.</param>
    /// <param name="disambiguator">The reading disambiguator.</param>
    /// <param name="aligner">An optional synchronous aligner for resolved readings.</param>
    /// <param name="cancellationToken">A token used to cancel assembly.</param>
    /// <returns>An AST preserving token and alignment order.</returns>
    public static async Task<DocumentNode> AssembleAsync(
        IAsyncEnumerable<MemoryToken> tokens,
        IPhoneticDisambiguator disambiguator,
        ISpanPhoneticAligner? aligner,
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
                    if (aligner is null)
                    {
                        children.Add(new RubyNode(
                            baseText,
                            token.ResolvedReading!,
                            token.SourceToken.Language,
                            token.SourceToken.ReadingSystem));
                    }
                    else
                    {
                        AppendAlignedNodes(children, token.SourceToken, token.ResolvedReading!, aligner);
                    }
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

    private static void AppendAlignedNodes(
        List<GlossAstNode> children,
        MemoryToken token,
        string resolvedReading,
        ISpanPhoneticAligner aligner)
    {
        if (!string.Equals(
            token.Language.Value,
            aligner.SupportedLanguage.Value,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The aligner language must match the token language.", nameof(aligner));
        }

        ReadOnlySpan<char> baseText = token.Text.Span;
        ReadOnlySpan<char> fullReading = resolvedReading.AsSpan();
        if (baseText.IsEmpty || fullReading.IsEmpty)
        {
            throw new InvalidOperationException("Resolved tokens and readings must be non-empty when alignment is requested.");
        }

        GlossSegmentOffsets[]? rented = null;
        Span<GlossSegmentOffsets> segments = stackalloc GlossSegmentOffsets[Math.Min(baseText.Length, 16)];
        if (baseText.Length > segments.Length)
        {
            rented = ArrayPool<GlossSegmentOffsets>.Shared.Rent(baseText.Length);
            segments = rented.AsSpan(0, baseText.Length);
        }

        try
        {
            var segmentCount = aligner.AlignToSpan(baseText, fullReading, segments);
            if (segmentCount <= 0 || segmentCount > segments.Length)
            {
                throw new InvalidOperationException("The aligner returned an invalid segment count.");
            }

            var nextBaseIndex = 0;
            var nextGlossIndex = 0;
            for (var index = 0; index < segmentCount; index++)
            {
                var segment = segments[index];
                if (segment.BaseStartIndex != nextBaseIndex
                    || segment.GlossStartIndex != nextGlossIndex
                    || segment.BaseLength <= 0
                    || segment.GlossLength < 0
                    || (segment.IsAnnotated && segment.GlossLength == 0)
                    || segment.BaseLength > baseText.Length - segment.BaseStartIndex
                    || segment.GlossLength > fullReading.Length - segment.GlossStartIndex)
                {
                    throw new InvalidOperationException("Alignment segments must partition the source and reading in order.");
                }

                var segmentBaseText = baseText.Slice(segment.BaseStartIndex, segment.BaseLength).ToString();
                if (segment.IsAnnotated)
                {
                    children.Add(new RubyNode(
                        segmentBaseText,
                        fullReading.Slice(segment.GlossStartIndex, segment.GlossLength).ToString(),
                        token.Language,
                        token.ReadingSystem));
                }
                else
                {
                    children.Add(new TextNode(segmentBaseText));
                }
                nextBaseIndex += segment.BaseLength;
                nextGlossIndex += segment.GlossLength;
            }

            if (nextBaseIndex != baseText.Length || nextGlossIndex != fullReading.Length)
            {
                throw new InvalidOperationException("Alignment segments must cover the complete source and reading.");
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<GlossSegmentOffsets>.Shared.Return(rented);
            }
        }
    }
}