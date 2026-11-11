/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

namespace PerformanceTest;

/// <summary>
///     What the IP layer costs on top of the core: one million IPv4 lookups on the real table, on the bare
///     <see cref="BitTrie{TKey,TPrefix,TValue}" /> inside an <see cref="IPNetworkLookup{TValue}" /> and through the
///     lookup. Both read the same trie: two tries built apart lie in different places in memory, which alone moved their
///     times apart by up to 9%. Set ROUTEVIEWS_DIR.
/// </summary>
public class LayerBenchmarks
{
    private BitTrie<Bits32, IPNetwork, int> _core = null!;
    private IPNetworkLookup<int> _lookup = null!;
    private Bits32[] _keys = null!;
    private uint[] _bits = null!;

    [GlobalSetup]
    public void Setup()
    {
        var table = RouteViews.IPv4;
        _lookup = RouteViews.Build(table);
        var trieField = typeof(IPPrefixLookup<IPNetwork, IPNetworkAdapter, int>).GetField("_ipv4Trie", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingFieldException("IPPrefixLookup", "_ipv4Trie");
        _core = (BitTrie<Bits32, IPNetwork, int>)trieField.GetValue(_lookup)!;
        _bits = table.IPv4Keys;
        _keys = _bits.Select(keyBits => new Bits32(keyBits)).ToArray();
    }

    [Benchmark(Baseline = true)]
    public long Core()
    {
        var trie = _core;
        long sum = 0;
        foreach (var key in _keys)
            sum += trie.TryGetMatch(in key, out var network, out var asn) ? network.PrefixLength + asn : -1;
        return sum;
    }

    [Benchmark]
    public long IPLayer()
    {
        var lookup = _lookup;
        long sum = 0;
        foreach (var key in _bits)
            sum += lookup.TryGetMatchIPv4(key, out var network, out var asn) ? network.PrefixLength + asn : -1;
        return sum;
    }
}
