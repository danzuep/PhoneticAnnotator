using BenchmarkDotNet.Attributes;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Benchmarks;

[MemoryDiagnoser]
public class AlignmentBenchmarks
{
    private readonly JapaneseOkuriganaAligner _aligner = new();
    private string _source = "";
    private string _reading = "";
    private GlossSegmentOffsets[] _segments = [];

    [Params(1, 64, 2048)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup allocations and reusable output buffers are excluded from Bytes/Op.
        _source = string.Concat(Enumerable.Repeat("食べる", Repetitions));
        _reading = string.Concat(Enumerable.Repeat("タベル", Repetitions));
        _segments = new GlossSegmentOffsets[_source.Length];
    }

    [Benchmark]
    public int AlignOkurigana() => _aligner.AlignToSpan(_source, _reading, _segments);
}
