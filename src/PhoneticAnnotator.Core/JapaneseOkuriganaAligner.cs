using System.Buffers;
using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Aligns unique literal kana runs as plain text and pairs the intervening kanji with readings.
/// </summary>
public sealed class JapaneseOkuriganaAligner : ISpanPhoneticAligner
{
    /// <inheritdoc />
    public LanguageCode SupportedLanguage { get; } = new("ja-JP");

    /// <inheritdoc />
    public int AlignToSpan(
        ReadOnlySpan<char> baseText,
        ReadOnlySpan<char> fullReading,
        Span<GlossSegmentOffsets> destinationBuffer)
    {
        if (baseText.IsEmpty || fullReading.IsEmpty)
        {
            return 0;
        }

        if (destinationBuffer.IsEmpty)
        {
            throw new ArgumentException("The destination must hold at least one segment.", nameof(destinationBuffer));
        }

        var segmentCount = 0;
        var baseCursor = 0;
        var glossCursor = 0;
        var scanIndex = 0;
        var foundKana = false;

        while (scanIndex < baseText.Length)
        {
            var runStart = scanIndex;
            while (scanIndex < baseText.Length && !TryReadKana(baseText, scanIndex, out _))
            {
                scanIndex += GetRuneLength(baseText, scanIndex);
            }

            if (scanIndex == baseText.Length)
            {
                break;
            }

            var kanaStart = scanIndex;
            while (scanIndex < baseText.Length && TryReadKana(baseText, scanIndex, out var consumed))
            {
                scanIndex += consumed;
            }

            var kana = baseText[kanaStart..scanIndex];
            var remainingReading = fullReading[glossCursor..];
            var relativeMatch = remainingReading.IndexOf(kana, StringComparison.Ordinal);
            if (relativeMatch < 0
                || remainingReading[(relativeMatch + kana.Length)..].IndexOf(kana, StringComparison.Ordinal) >= 0)
            {
                return WriteWholeToken(baseText, fullReading, destinationBuffer);
            }

            var matchedReadingStart = glossCursor + relativeMatch;
            var basePrefixLength = kanaStart - baseCursor;
            var glossPrefixLength = matchedReadingStart - glossCursor;
            if (basePrefixLength == 0 && glossPrefixLength > 0)
            {
                return WriteWholeToken(baseText, fullReading, destinationBuffer);
            }

            if (basePrefixLength > 0)
            {
                if (!TryWriteSegment(
                    destinationBuffer,
                    ref segmentCount,
                    baseCursor,
                    basePrefixLength,
                    glossCursor,
                    glossPrefixLength,
                    isAnnotated: glossPrefixLength > 0))
                {
                    return WriteWholeToken(baseText, fullReading, destinationBuffer);
                }
            }

            if (!TryWriteSegment(
                destinationBuffer,
                ref segmentCount,
                kanaStart,
                kana.Length,
                matchedReadingStart,
                kana.Length,
                isAnnotated: false))
            {
                return WriteWholeToken(baseText, fullReading, destinationBuffer);
            }

            baseCursor = scanIndex;
            glossCursor = matchedReadingStart + kana.Length;
            foundKana = true;
        }

        if (!foundKana)
        {
            return WriteWholeToken(baseText, fullReading, destinationBuffer);
        }

        var remainingBaseLength = baseText.Length - baseCursor;
        var remainingGlossLength = fullReading.Length - glossCursor;
        if (remainingBaseLength == 0 && remainingGlossLength > 0)
        {
            return WriteWholeToken(baseText, fullReading, destinationBuffer);
        }

        if (remainingBaseLength > 0)
        {
            if (!TryWriteSegment(
                destinationBuffer,
                ref segmentCount,
                baseCursor,
                remainingBaseLength,
                glossCursor,
                remainingGlossLength,
                isAnnotated: remainingGlossLength > 0))
            {
                return WriteWholeToken(baseText, fullReading, destinationBuffer);
            }
        }

        return segmentCount;
    }

    private static bool TryWriteSegment(
        Span<GlossSegmentOffsets> destinationBuffer,
        ref int segmentCount,
        int baseStart,
        int baseLength,
        int glossStart,
        int glossLength,
        bool isAnnotated)
    {
        if (segmentCount == destinationBuffer.Length)
        {
            return false;
        }

        destinationBuffer[segmentCount++] = new GlossSegmentOffsets(
            baseStart,
            baseLength,
            glossStart,
            glossLength,
            isAnnotated);
        return true;
    }

    private static int WriteWholeToken(
        ReadOnlySpan<char> baseText,
        ReadOnlySpan<char> fullReading,
        Span<GlossSegmentOffsets> destinationBuffer)
    {
        destinationBuffer[0] = new GlossSegmentOffsets(0, baseText.Length, 0, fullReading.Length);
        return 1;
    }

    private static int GetRuneLength(ReadOnlySpan<char> text, int startIndex) =>
        Rune.DecodeFromUtf16(text[startIndex..], out _, out var consumed) == OperationStatus.Done
            ? consumed
            : 1;

    private static bool TryReadKana(ReadOnlySpan<char> text, int startIndex, out int consumed)
    {
        if (Rune.DecodeFromUtf16(text[startIndex..], out var rune, out consumed) != OperationStatus.Done)
        {
            consumed = 1;
            return false;
        }

        var value = rune.Value;
        return value is >= 0x3040 and <= 0x309F
            or >= 0x30A0 and <= 0x30FF
            or >= 0x31F0 and <= 0x31FF
            or >= 0xFF66 and <= 0xFF9F
            or >= 0x1B000 and <= 0x1B16F;
    }
}