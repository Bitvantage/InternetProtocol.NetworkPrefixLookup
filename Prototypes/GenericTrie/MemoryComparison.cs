/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;
using Bitvantage.InternetProtocol;

namespace GenericTrie;

/// <summary>
///     Memory kept by each design after building, measured as the growth of the managed heap after full collections.
///     The callers' own key objects (NetworkPrefix objects for the current library, IPNetwork values and their IPAddress
///     objects for the others) are created before the measurement and are not counted.
/// </summary>
public static class MemoryComparison
{
    private static long Retained(Func<object> build)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        var result = build();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var after = GC.GetTotalMemory(true);
        GC.KeepAlive(result);
        return after - before;
    }

    public static void Run()
    {
        // create all shared test data first, so that no measurement window includes it
        GC.KeepAlive(Data.Prefixes);
        GC.KeepAlive(Data.IPNetworks);
        GC.KeepAlive(WidthData.Prefix32);
        GC.KeepAlive(RealData.V4);
        GC.KeepAlive(RealData.V6);

        Console.WriteLine("| Table | Design | Retained | Bytes per prefix |");
        Console.WriteLine("| --- | --- | --- | --- |");

        // random IPv4 benchmark data: NetworkPrefix inputs already exist in Data.Prefixes
        Report("Random IPv4, 96,944 prefixes", Data.Prefixes.Count, Data.Prefixes,
            () =>
            {
                var lookup = new NetworkPrefixLookup<string>();
                foreach (var networkPrefix in Data.Prefixes)
                    lookup.TryAdd(networkPrefix, string.Empty);
                return lookup;
            },
            Optimization.BuildCached,
            trim =>
            {
                var trie = Optimization.BuildPooled();
                if (trim)
                    trie.TrimExcess();
                return trie;
            },
            (trim, bits) =>
            {
                var trie = Optimization.BuildPooled(NodeOrder.DepthFirst, bits);
                if (trim)
                    trie.TrimExcess();
                return trie;
            },
            16);

        foreach (var (name, table, bits) in new (string, object, int)[] { ("Real IPv4", RealData.V4, 20), ("Real IPv6", RealData.V6, 20) })
        {
            if (table is RealData.Table<Bits32> ipv4Table)
                ReportReal(name, ipv4Table, bits);
            else
                ReportReal(name, (RealData.Table<Bits128>)table, bits);
        }
    }

    private static void ReportReal<TKey>(string name, RealData.Table<TKey> table, int bits) where TKey : struct, IBitKey<TKey>
    {
        var width = typeof(TKey) == typeof(Bits32) ? 32 : 128;
        var count = table.Bits.Length;
        foreach (var threshold in new[] { 8, 32 })
        {
            var bytes = Retained(() =>
            {
                var trie = new SnapshotTrie<TKey, IPNetwork, int>(width, 16, 8, threshold);
                trie.BeginBulkLoad();
                for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
                    trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
                trie.EndBulkLoad();
                return trie;
            });
            Console.WriteLine($"| {name}, {count:N0} prefixes | SnapshotTrie, bulk loaded, multi-level 16/8/{threshold} | {bytes / 1048576.0:F1} MB | {bytes / (double)count:F0} |");
        }

        var prefixes = table.Networks.Select(network => new NetworkPrefix(network.BaseAddress, network.PrefixLength)).ToArray();
        Report($"{name}, {table.Bits.Length:N0} prefixes", table.Bits.Length, prefixes,
            () =>
            {
                var lookup = new NetworkPrefixLookup<int>();
                for (var prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
                    lookup.TryAdd(prefixes[prefixIndex], table.Asns[prefixIndex]);
                return lookup;
            },
            () => RealDataChecks.BuildCached(table),
            trim =>
            {
                var trie = new PooledTrie<TKey, IPNetwork, int>();
                for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
                    trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
                if (trim)
                    trie.TrimExcess();
                return trie;
            },
            (trim, indexBits) =>
            {
                var trie = RealDataChecks.BuildPooled(table, indexBits);
                if (trim)
                    trie.TrimExcess();
                return trie;
            },
            bits);
    }

    private static void Report(string name, int count, object keepAlive, Func<object> current, Func<object> cached,
        Func<bool, object> pooled, Func<bool, int, object> indexed, int indexBits)
    {
        void Row(string design, long bytes) =>
            Console.WriteLine($"| {name} | {design} | {bytes / 1048576.0:F1} MB | {bytes / (double)count:F0} |");

        Row("Current library", Retained(current));
        Row("Baseline: CachedKeyTrie", Retained(cached));
        Row("Node pool, as built", Retained(() => pooled(false)));
        Row("Node pool, trimmed", Retained(() => pooled(true)));
        Row($"Node pool, depth-first, {indexBits}-bit index, as built", Retained(() => indexed(false, indexBits)));
        Row($"Node pool, depth-first, {indexBits}-bit index, trimmed", Retained(() => indexed(true, indexBits)));
        GC.KeepAlive(keepAlive);

        dynamic trimmed = indexed(true, indexBits);
        var (nodes, lengths, entries, index) = ((long, long, long, long))trimmed.MemoryBreakdown();
        Console.WriteLine($"|   | breakdown, trimmed: nodes {nodes / 1048576.0:F1} MB, lengths {lengths / 1048576.0:F1} MB, prefixes and values {entries / 1048576.0:F1} MB, index {index / 1048576.0:F1} MB | | |");
    }
}
