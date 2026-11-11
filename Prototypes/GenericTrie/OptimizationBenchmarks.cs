/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace GenericTrie;

/// <summary>Lookup speed of each optimization step, 1,000,000 precomputed 32-bit keys against 97,000 prefixes.</summary>
[MemoryDiagnoser]
public class OptimizationComparison
{
    private static readonly CachedKeyTrie<Bits32, IPNetwork, string> Cached = Optimization.BuildCached();
    private static readonly InlineChildTrie<Bits32, IPNetwork, string> Inline = Optimization.BuildInline();
    private static readonly PooledTrie<Bits32, IPNetwork, string> Pooled = Optimization.BuildPooled();
    private static readonly PooledTrie<Bits32, IPNetwork, string> PooledBreadthFirst = Optimization.BuildPooled(NodeOrder.BreadthFirst);
    private static readonly PooledTrie<Bits32, IPNetwork, string> PooledDepthFirst = Optimization.BuildPooled(NodeOrder.DepthFirst);
    private static readonly PooledTrie<Bits32, IPNetwork, string> Index12 = Optimization.BuildPooled(NodeOrder.DepthFirst, 12);
    private static readonly PooledTrie<Bits32, IPNetwork, string> Index16 = Optimization.BuildPooled(NodeOrder.DepthFirst, 16);
    private static readonly PooledTrie<Bits32, IPNetwork, string> Index17 = Optimization.BuildPooled(NodeOrder.DepthFirst, 17);

    [Benchmark(Baseline = true), IterationCount(30)]
    public int A_Baseline_CachedKeyTrie()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += Cached.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int B_InlineChildren()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += Inline.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int C_NodePool()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += Pooled.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int D_NodePool_BreadthFirst()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += PooledBreadthFirst.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int E_NodePool_DepthFirst()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += PooledDepthFirst.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int F_DepthFirst_Index12()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += Index12.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int G_DepthFirst_Index16()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += Index16.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int H_DepthFirst_Index17()
    {
        var keys = WidthData.Key32;
        var sum = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            sum += Index17.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
        return sum;
    }
}

/// <summary>End to end from IPAddress objects: the current library against the optimized design.</summary>
[MemoryDiagnoser]
public class EndToEndComparison
{
    private static readonly Bitvantage.InternetProtocol.NetworkPrefixLookup<string> Current = BuildCurrent();
    private static readonly PooledTrie<Bits32, IPNetwork, string> Optimized = Optimization.BuildPooled(NodeOrder.DepthFirst, 17);

    private static Bitvantage.InternetProtocol.NetworkPrefixLookup<string> BuildCurrent()
    {
        var lookup = new Bitvantage.InternetProtocol.NetworkPrefixLookup<string>();
        foreach (var networkPrefix in Data.Prefixes)
            lookup.TryAdd(networkPrefix, string.Empty);
        return lookup;
    }

    [Benchmark(Baseline = true), IterationCount(30)]
    public int CurrentLibrary()
    {
        var sum = 0;
        foreach (var address in Data.Addresses)
            sum += Current.TryGetMatch(address, out var match) ? match.Prefix.Length + match.Value!.Length : -1;
        return sum;
    }

    [Benchmark, IterationCount(30)]
    public int Optimized_TryWriteBytes() { var sum = 0; foreach (var address in Data.Addresses) { var key = new Bits32(IPBits.V4(address)); sum += Optimized.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1; } return sum; }

    [Benchmark, IterationCount(30)]
    public int Optimized_AddressProperty() { var sum = 0; foreach (var address in Data.Addresses) { var key = new Bits32(Optimization.FromAddressProperty(address)); sum += Optimized.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1; } return sum; }

    [Benchmark, IterationCount(30)]
    public int Optimized_PrivateField() { var sum = 0; foreach (var address in Data.Addresses) { var key = new Bits32(Optimization.FromPrivateField(address)); sum += Optimized.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1; } return sum; }
}

/// <summary>Building a table from 97,000 prefixes.</summary>
[MemoryDiagnoser]
public class BuildComparison
{
    [Benchmark(Baseline = true), IterationCount(30)]
    public object CurrentLibrary()
    {
        var lookup = new Bitvantage.InternetProtocol.NetworkPrefixLookup<string>();
        foreach (var networkPrefix in Data.Prefixes)
            lookup.TryAdd(networkPrefix, string.Empty);
        return lookup;
    }

    [Benchmark, IterationCount(30)]
    public object CachedKeyTrie() => Optimization.BuildCached();

    [Benchmark, IterationCount(30)]
    public object NodePool() => Optimization.BuildPooled();

    [Benchmark, IterationCount(30)]
    public object NodePool_DepthFirst() => Optimization.BuildPooled(NodeOrder.DepthFirst);

    [Benchmark, IterationCount(30)]
    public object NodePool_DepthFirst_Index17() => Optimization.BuildPooled(NodeOrder.DepthFirst, 17);
}
