using System.Runtime.CompilerServices;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Resolves only tokens with exactly one candidate and never guesses among readings.
/// </summary>
public sealed class CandidateOnlyDisambiguator : IPhoneticDisambiguator
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
                _ => ReadingResolution.Ambiguous
            };
            var reading = status == ReadingResolution.Resolved ? token.ReadingCandidates[0] : null;
            yield return new DisambiguatedToken(token, status, reading);
        }
    }
}