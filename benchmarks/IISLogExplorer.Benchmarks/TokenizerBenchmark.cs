using BenchmarkDotNet.Attributes;
using IISLogExplorer.Core.Parsing;

namespace IISLogExplorer.Benchmarks;

[MemoryDiagnoser]
public class TokenizerBenchmark
{
    private string _line = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _line = BenchmarkData.SampleLine(0);
    }

    [Benchmark]
    public int Tokenize()
    {
        var total = 0;
        for (var index = 0; index < 10_000; index++)
        {
            total += W3cLineTokenizer.Tokenize(_line).Count;
        }

        return total;
    }
}