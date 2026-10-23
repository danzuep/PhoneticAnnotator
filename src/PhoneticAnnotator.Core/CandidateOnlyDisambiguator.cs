using System.Runtime.CompilerServices;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Resolves single candidates, optionally selecting the first of multiple readings.
/// </summary>
public sealed class CandidateOnlyDisambiguator(bool pickFirstReading = false) : IPhoneticDisambiguator
{
    /// <inheritdoc />
    public async IAsyncEnumerable<DisambiguatedToken> DisambiguateStreamAsync(
        IAsyncEnumerable<MemoryToken> tokens,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        await foreach (var token in tokens.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var status = token.ReadingCandidates.Count switch
            {
                0 => ReadingResolution.NoReading,
                1 => ReadingResolution.Resolved,
                _ when pickFirstReading => ReadingResolution.Resolved,
                _ => ReadingResolution.Ambiguous
            };
            var reading = status == ReadingResolution.Resolved ? token.ReadingCandidates[0] : null;
            yield return new DisambiguatedToken(token, status, reading);
        }
    }
}