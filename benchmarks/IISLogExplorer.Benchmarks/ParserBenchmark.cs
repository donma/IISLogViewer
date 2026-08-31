using BenchmarkDotNet.Attributes;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;

namespace IISLogExplorer.Benchmarks;

[MemoryDiagnoser]
public class ParserBenchmark
{
    private IisW3cLogParser _parser = null!;
    private string _path = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        _path = BenchmarkData.SampleLogPath(100_000);
    }

    [Benchmark]
    public async Task<int> ParseAll()
    {
        var count = 0;
        await foreach (var _ in _parser.ParseAsync(_path, 1))
        {
            count++;
        }

        return count;
    }
}