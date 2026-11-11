/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using BenchmarkDotNet.Attributes;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

namespace PerformanceTest;

/// <summary>Lookups in small frozen tables: the layouts' full roots against the root sized from the number of prefixes.</summary>
public class FrozenRootBenchmarks
{
    [Params(64, 256, 1000, 4000)]
    public int Prefixes;

    private FrozenIPNetworkLookup<string> _fullRoot = null!;
    private FrozenIPNetworkLookup<string> _sizedRoot = null!;
    private FrozenIPNetworkLookup<string> _compressedFullRoot = null!;
    private FrozenIPNetworkLookup<string> _compressedSizedRoot = null!;
    private uint[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        var lookup = new IPNetworkLookup<string>(RandomTableBenchmarks.CreateNetworks().Take(Prefixes).Select(network => KeyValuePair.Create(network, string.Empty)));
        _fullRoot = lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed, RootBits = 16 });
        _sizedRoot = lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed });
        _compressedFullRoot = lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed, RootBits = 18 });
        _compressedSizedRoot = lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed });
        _keys = RandomTableBenchmarks.CreateAddresses().Select(IPAddressBits.ToIPv4Bits).ToArray();
        Console.WriteLine($"// {Prefixes} prefixes: leaf-pushed {_fullRoot.Bytes:N0} bytes full, {_sizedRoot.Bytes:N0} sized; " +
                          $"compressed {_compressedFullRoot.Bytes:N0} full, {_compressedSizedRoot.Bytes:N0} sized");
    }

    private static int Count(FrozenIPNetworkLookup<string> lookup, uint[] keys)
    {
        var found = 0;
        foreach (var key in keys)
            if (lookup.TryGetMatchValueIPv4(key, out _))
                found++;
        return found;
    }

    [Benchmark(Baseline = true)]
    public int LeafPushed_FullRoot() => Count(_fullRoot, _keys);

    [Benchmark]
    public int LeafPushed_SizedRoot() => Count(_sizedRoot, _keys);

    [Benchmark]
    public int Compressed_FullRoot() => Count(_compressedFullRoot, _keys);

    [Benchmark]
    public int Compressed_SizedRoot() => Count(_compressedSizedRoot, _keys);
}
