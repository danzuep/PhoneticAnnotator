using BenchmarkDotNet.Attributes;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Benchmarks;

[MemoryDiagnoser]
public class AlignmentBenchmarks
{
    private readonly JapaneseOkuriganaAligner _aligner = new();
    private string _source = "";
    private string _reading = "";
    private string _uniqueSource = "";
    private string _uniqueReading = "";
    private string _compoundSource = "";
    private string _compoundReading = "";
    private GlossSegmentOffsets[] _segments = [];

    [Params(1, 64, 2048)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup allocations and reusable output buffers are excluded from Bytes/Op.
        _source = string.Concat(Enumerable.Repeat("食べる", Repetitions));
        _reading = string.Concat(Enumerable.Repeat("タベル", Repetitions));
        _uniqueSource = new string('食', Repetitions) + "べる";
        _uniqueReading = new string('タ', Repetitions) + "ベル";
        _compoundSource = new string('生', Repetitions);
        _compoundReading = new string('な', Repetitions);
        _segments = new GlossSegmentOffsets[_source.Length];
    }

    [Benchmark]
    public int AmbiguousKanaFallback() => _aligner.AlignToSpan(_source, _reading, _segments);

    [Benchmark]
    public int UniqueOkurigana() => _aligner.AlignToSpan(_uniqueSource, _uniqueReading, _segments);

    [Benchmark]
    public int WholeCompound() => _aligner.AlignToSpan(_compoundSource, _compoundReading, _segments);
}
