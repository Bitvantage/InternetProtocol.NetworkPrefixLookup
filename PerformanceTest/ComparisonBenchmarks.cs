/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;
using BenchmarkDotNet.Attributes;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

namespace PerformanceTest;

/// <summary>
///     Version 3 on the tables of <see cref="ComparisonData" />. PerformanceTest.V2 runs the same benchmarks, with the
///     same names, against version 2.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ComparisonBenchmarks
{
    [ParamsSource(nameof(Names))]
    public string Table = "";

    public static IEnumerable<string> Names => ComparisonData.Names;

    private ComparisonData.Table _data = null!;
    private IPNetwork[] _networks = null!;
    private IPNetworkLookup<int> _lookup = null!;
    private FrozenIPNetworkLookup<int> _frozen = null!;

    /// <summary>A table's prefixes as <see cref="IPNetwork" /> values; version 2 makes its own NetworkPrefix objects.</summary>
    public static IPNetwork[] Networks(ComparisonData.Table data) => data.Prefixes.Select(prefix => new IPNetwork(prefix.Address, prefix.Length)).ToArray();

    [GlobalSetup]
    public void Setup()
    {
        _data = ComparisonData.Get(Table);
        _networks = Networks(_data);
        _lookup = Build(batch: true);
        _frozen = _lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed });
    }

    private IPNetworkLookup<int> Build(bool batch)
    {
        var lookup = new IPNetworkLookup<int>();
        var writeBatch = batch ? lookup.BeginBatch() : null;
        for (var networkIndex = 0; networkIndex < _networks.Length; networkIndex++)
            lookup.TryAdd(_networks[networkIndex], _data.Values[networkIndex]);
        writeBatch?.Commit();
        return lookup;
    }

    [Benchmark]
    public long Lookup()
    {
        var lookup = _lookup;
        long sum = 0;
        foreach (var address in _data.Addresses)
            sum += lookup.TryGetMatch(address, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
        return sum;
    }

    [Benchmark]
    public long Lookup_Frozen()
    {
        var lookup = _frozen;
        long sum = 0;
        foreach (var address in _data.Addresses)
            sum += lookup.TryGetMatch(address, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
        return sum;
    }

    [Benchmark]
    public object Build_OneAtATime() => Build(batch: false);

    [Benchmark]
    public object Build_Batch() => Build(batch: true);
}
