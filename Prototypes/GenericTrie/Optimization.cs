/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace GenericTrie;

/// <summary>
///     Builders, correctness checks and quick timings for the optimization steps. All variants store the benchmark's
///     IPv4 prefixes as IPNetwork values with string values, and look up the same 1,000,000 precomputed 32-bit keys.
/// </summary>
public static class Optimization
{
    public static CachedKeyTrie<Bits32, IPNetwork, string> BuildCached()
    {
        var trie = new CachedKeyTrie<Bits32, IPNetwork, string>();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix32.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        return trie;
    }

    public static InlineChildTrie<Bits32, IPNetwork, string> BuildInline()
    {
        var trie = new InlineChildTrie<Bits32, IPNetwork, string>();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix32.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        return trie;
    }

    public static PooledTrie<Bits32, IPNetwork, string> BuildPooled(NodeOrder? order = null, int indexBits = 0, int count = -1, bool candidate = false)
    {
        var trie = new PooledTrie<Bits32, IPNetwork, string>();
        if (count < 0)
            count = WidthData.Prefix32.Length;
        for (var prefixIndex = 0; prefixIndex < count; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        if (count < WidthData.Prefix32.Length)
            trie.TryAdd(new Bits32(0), 0, IPNetwork.Parse("0.0.0.0/0"), string.Empty); // small tables get a default route
        if (order != null)
            trie.Relayout(order.Value);
        if (indexBits > 0)
        {
            if (candidate)
                trie.BuildCandidateIndex(indexBits);
            else
                trie.BuildIndex(indexBits);
        }
        return trie;
    }

    public static InheritedBestTrie<Bits32, IPNetwork, string> BuildInherited(int indexBits = 0, int count = -1, bool relayout = true)
    {
        var trie = new InheritedBestTrie<Bits32, IPNetwork, string>();
        if (count < 0)
            count = WidthData.Prefix32.Length;
        for (var prefixIndex = 0; prefixIndex < count; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        if (count < WidthData.Prefix32.Length)
            trie.TryAdd(new Bits32(0), 0, IPNetwork.Parse("0.0.0.0/0"), string.Empty);
        if (relayout)
            trie.RelayoutDepthFirst();
        if (indexBits > 0)
            trie.BuildIndex(indexBits);
        return trie;
    }

    private static IPNetwork[] Expected()
    {
        var reference = WidthData.BuildRef();
        var expected = new IPNetwork[WidthData.KeyReference.Length];
        for (var keyIndex = 0; keyIndex < expected.Length; keyIndex++)
        {
            reference.TryGetMatch(WidthData.KeyReference[keyIndex], out var index, out _);
            expected[keyIndex] = Data.IPNetworks[index];
        }
        return expected;
    }

    public static void Verify()
    {
        var expected = Expected();
        var keys = WidthData.Key32;

        void Check(string name, Func<int, IPNetwork?> lookup)
        {
            var mismatches = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                if (lookup(keyIndex) != expected[keyIndex])
                    mismatches++;
            Console.WriteLine($"  {name,-28} mismatches: {mismatches}");
        }

        void CheckBatch(string name, PooledTrie<Bits32, IPNetwork, string> trie, int lanes, bool prefetch)
        {
            var results = new int[keys.Length];
            trie.GetMatches(keys, results, lanes, prefetch);
            Check(name, keyIndex => results[keyIndex] >= 0 ? trie.GetPrefix(results[keyIndex]) : null);
        }

        Console.WriteLine($"verifying against the reference for {keys.Length:N0} keys");
        var cached = BuildCached();
        Check("cached (baseline)", keyIndex => cached.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
        var inline = BuildInline();
        Check("inline children", keyIndex => inline.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
        var pooled = BuildPooled();
        Check("pooled", keyIndex => pooled.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
        Console.WriteLine($"  pooled node size: {pooled.NodeSizeInBytes} bytes, nodes: {pooled.NodeCount:N0}");
        var breadthFirst = BuildPooled(NodeOrder.BreadthFirst);
        Check("pooled, breadth-first", keyIndex => breadthFirst.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
        var depthFirst = BuildPooled(NodeOrder.DepthFirst);
        Check("pooled, depth-first", keyIndex => depthFirst.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
        foreach (var bits in new[] { 8, 12, 16, 20 })
        {
            var indexed = BuildPooled(NodeOrder.DepthFirst, bits);
            Check($"pooled, depth-first, index {bits}", keyIndex => indexed.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
        }
        foreach (var bits in new[] { 8, 12, 15, 16, 17 })
        {
            var indexed = BuildPooled(NodeOrder.DepthFirst, bits, candidate: true);
            Check($"candidate index {bits}", keyIndex => indexed.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
            var small = BuildPooled(NodeOrder.DepthFirst, bits, 1_000, candidate: true);
            var smallReference = BuildPooled(NodeOrder.DepthFirst, 0, 1_000);
            var smallMismatches = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                small.TryGetMatch(in keys[keyIndex], out var smallPrefix, out _);
                smallReference.TryGetMatch(in keys[keyIndex], out var referencePrefix, out _);
                if (smallPrefix != referencePrefix)
                    smallMismatches++;
            }
            Console.WriteLine($"  candidate index {bits}, small table   mismatches: {smallMismatches}");
        }
        foreach (var bits in new[] { 0, 12, 16, 17, 18 })
        {
            var inherited = BuildInherited(bits);
            Check($"inherited best, index {bits}", keyIndex => inherited.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);
            var small = BuildInherited(bits, 1_000);
            var smallReference = BuildPooled(NodeOrder.DepthFirst, 0, 1_000);
            var smallMismatches = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                small.TryGetMatch(in keys[keyIndex], out var smallPrefix, out _);
                smallReference.TryGetMatch(in keys[keyIndex], out var referencePrefix, out _);
                if (smallPrefix != referencePrefix)
                    smallMismatches++;
            }
            Console.WriteLine($"  inherited best, index {bits}, small  mismatches: {smallMismatches}");
        }

        // insertion order must not matter: build from a shuffled order and compare
        var shuffled = Enumerable.Range(0, WidthData.Prefix32.Length).OrderBy(prefixIndex => (prefixIndex * 2654435761u) % 1000003).ToArray();
        var shuffledTrie = new InheritedBestTrie<Bits32, IPNetwork, string>();
        foreach (var prefixIndex in shuffled)
            shuffledTrie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        shuffledTrie.BuildIndex(16);
        Check("inherited best, shuffled inserts", keyIndex => shuffledTrie.TryGetMatch(in keys[keyIndex], out var prefix, out _) ? prefix : null);

        var batchTrie = BuildPooled(NodeOrder.DepthFirst, 16);
        CheckBatch("batch 8 lanes", batchTrie, 8, false);
        CheckBatch("batch 8 lanes, prefetch", batchTrie, 8, true);
        CheckBatch("batch 3 lanes, prefetch", batchTrie, 3, true);
        var batchNoIndex = BuildPooled(NodeOrder.DepthFirst);
        CheckBatch("batch 8 lanes, no index", batchNoIndex, 8, true);

        foreach (var bits in new[] { 0, 12, 16, 17, 18 })
        {
            var trie = BuildPooled(NodeOrder.DepthFirst, bits);
            long visited = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                visited += trie.CountVisited(in keys[keyIndex]);
            Console.WriteLine($"  index {bits,2}: average nodes visited per lookup {visited / (double)keys.Length:F2}, entries pointing at the sentinel {trie.FinalEntryFraction:P0}");
        }
        foreach (var bits in new[] { 10, 12 })
        {
            var trie = BuildPooled(NodeOrder.DepthFirst, bits, 1_000);
            Console.WriteLine($"  small table, index {bits}: entries pointing at the sentinel {trie.FinalEntryFraction:P0}");
        }

        var conversions = 0;
        foreach (var address in Data.Addresses)
            if (FromAddressProperty(address) != IPBits.V4(address) || FromPrivateField(address) != IPBits.V4(address))
                conversions++;
        Console.WriteLine($"  address conversions disagreeing with TryWriteBytes: {conversions}");
    }

    private static CachedKeyTrie<Bits128, IPNetwork, string> Build128Cached()
    {
        var trie = new CachedKeyTrie<Bits128, IPNetwork, string>();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix128.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix128[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        return trie;
    }

    private static PooledTrie<Bits128, IPNetwork, string> Build128Pooled(int bits)
    {
        var trie = new PooledTrie<Bits128, IPNetwork, string>();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix128.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix128[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        trie.Relayout(NodeOrder.DepthFirst);
        if (bits > 0)
            trie.BuildIndex(bits);
        return trie;
    }

    private static PooledTrie<Bits32, IPNetwork, string> BuildNoSkip(int bits)
    {
        var trie = BuildPooled(NodeOrder.DepthFirst);
        trie.BuildIndex(bits, skipFinal: false);
        return trie;
    }

    private static CachedKeyTrie<Bits32, IPNetwork, string> BuildSmallCached()
    {
        var trie = new CachedKeyTrie<Bits32, IPNetwork, string>();
        for (var prefixIndex = 0; prefixIndex < 1_000; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        trie.TryAdd(new Bits32(0), 0, IPNetwork.Parse("0.0.0.0/0"), string.Empty);
        return trie;
    }

    // IPAddress.Address is obsolete because it only works for IPv4, which is exactly the case here
#pragma warning disable CS0618
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FromAddressProperty(IPAddress address) => BinaryPrimitives.ReverseEndianness((uint)address.Address);
#pragma warning restore CS0618

    // reads IPAddress's private IPv4 field directly; depends on a runtime implementation detail
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_addressOrScopeId")]
    private static extern ref uint AddressField(IPAddress address);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FromPrivateField(IPAddress address) => BinaryPrimitives.ReverseEndianness(AddressField(address));

    /// <summary>Times one variant in a fresh process: the fastest of 15 runs of 1M lookups, after warm-up.</summary>
    public static void Time(string variant)
    {
        var keys = WidthData.Key32;
        Func<int> run = variant switch
        {
            "cached" => Single(BuildCached(), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            "inline" => Single(BuildInline(), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            "pooled" => Single(BuildPooled(), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            "pooled-bfs" => Single(BuildPooled(NodeOrder.BreadthFirst), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            "cached128" => Single128(Build128Cached()),
            "pooled128-dfs" => Single128(Build128Pooled(0)),
            "pooled128-index16" => Single128(Build128Pooled(16)),
            "noskip17" => Single(BuildNoSkip(17), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            "pooled-dfs" => Single(BuildPooled(NodeOrder.DepthFirst), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            _ when variant.StartsWith("index") => Single(BuildPooled(NodeOrder.DepthFirst, int.Parse(variant[5..])), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            // small table: the first 1,000 prefixes plus a default route; small-index0 means no index
            _ when variant.StartsWith("cindex") => Single(BuildPooled(NodeOrder.DepthFirst, int.Parse(variant[6..]), candidate: true), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            _ when variant.StartsWith("small-cindex") => Single(BuildPooled(NodeOrder.DepthFirst, int.Parse(variant[12..]), 1_000, candidate: true), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            _ when variant.StartsWith("inherit") && !variant.StartsWith("inherit-small") => Single(BuildInherited(int.Parse(variant[7..])), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            _ when variant.StartsWith("inherit-small") => Single(BuildInherited(int.Parse(variant[13..]), 1_000), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            _ when variant.StartsWith("small-index") => Single(BuildPooled(NodeOrder.DepthFirst, int.Parse(variant[11..]), 1_000), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            "small-cached" => Single(BuildSmallCached(), (trie, keyIndex) => trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1),
            // end to end from IPAddress, with a 16-bit index, using different address conversions
            "e2e-trywritebytes" => EndToEnd(IPBits.V4),
            "e2e-address" => EndToEnd(FromAddressProperty),
            "e2e-accessor" => EndToEnd(FromPrivateField),
            "e2e-current" => Current(),
            _ when variant.StartsWith("batch") => Batch(variant),
            _ => throw new ArgumentException(variant)
        };

        for (var warmup = 0; warmup < 5; warmup++)
            run();
        var best = double.MaxValue;
        for (var trial = 0; trial < 15; trial++)
        {
            var stopwatch = Stopwatch.StartNew();
            run();
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"{variant,-24} best of 15: {best,7:F1} ms per 1M lookups");

        Func<int> EndToEnd(Func<IPAddress, uint> convert)
        {
            var trie = BuildPooled(NodeOrder.DepthFirst, 16);
            var addresses = Data.Addresses;
            return () =>
            {
                var sum = 0;
                foreach (var address in addresses)
                {
                    var key = new Bits32(convert(address));
                    sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
                }
                return sum;
            };
        }

        Func<int> Current()
        {
            var lookup = new Bitvantage.InternetProtocol.NetworkPrefixLookup<string>();
            foreach (var networkPrefix in Data.Prefixes)
                lookup.TryAdd(networkPrefix, string.Empty);
            var addresses = Data.Addresses;
            return () =>
            {
                var sum = 0;
                foreach (var address in addresses)
                    sum += lookup.TryGetMatch(address, out var match) ? match.Prefix.Length + match.Value!.Length : -1;
                return sum;
            };
        }

        Func<int> Single128(dynamic trie)
        {
            var keys128 = WidthData.Key128;
            if (trie is CachedKeyTrie<Bits128, IPNetwork, string> cachedTrie)
            {
                return () =>
                {
                    var sum = 0;
                    for (var keyIndex = 0; keyIndex < keys128.Length; keyIndex++)
                        sum += cachedTrie.TryGetMatch(in keys128[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
                    return sum;
                };
            }
            var pooledTrie = (PooledTrie<Bits128, IPNetwork, string>)trie;
            return () =>
            {
                var sum = 0;
                for (var keyIndex = 0; keyIndex < keys128.Length; keyIndex++)
                    sum += pooledTrie.TryGetMatch(in keys128[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value.Length : -1;
                return sum;
            };
        }

        Func<int> Single<T>(T trie, Func<T, int, int> lookup) => () =>
        {
            var sum = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                sum += lookup(trie, keyIndex);
            return sum;
        };

        // variant name: batch<lanes>[p][-noindex], for example batch8p or batch8p-noindex
        Func<int> Batch(string name)
        {
            var noIndex = name.EndsWith("-noindex");
            var specification = noIndex ? name[5..^8] : name[5..];
            var prefetch = specification.EndsWith('p');
            var lanes = int.Parse(prefetch ? specification[..^1] : specification);
            var trie = BuildPooled(NodeOrder.DepthFirst, noIndex ? 0 : 16);
            var results = new int[keys.Length];
            return () =>
            {
                trie.GetMatches(keys, results, lanes, prefetch);
                var sum = 0;
                for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
                    sum += results[resultIndex] >= 0 ? trie.GetPrefix(results[resultIndex]).PrefixLength + trie.GetValue(results[resultIndex]).Length : -1;
                return sum;
            };
        }
    }
}
