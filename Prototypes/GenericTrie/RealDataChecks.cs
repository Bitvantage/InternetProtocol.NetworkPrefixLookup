/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Diagnostics;
using System.Net;
using Bitvantage.InternetProtocol;

namespace GenericTrie;

/// <summary>Correctness checks and quick timings on the real RouteViews tables.</summary>
public static class RealDataChecks
{
    public static CachedKeyTrie<TKey, IPNetwork, int> BuildCached<TKey>(RealData.Table<TKey> table) where TKey : struct, IBitKey<TKey>
    {
        var trie = new CachedKeyTrie<TKey, IPNetwork, int>();
        for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        return trie;
    }

    public static PooledTrie<TKey, IPNetwork, int> BuildPooled<TKey>(RealData.Table<TKey> table, int indexBits) where TKey : struct, IBitKey<TKey>
    {
        var trie = new PooledTrie<TKey, IPNetwork, int>();
        for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        trie.Relayout(NodeOrder.DepthFirst);
        if (indexBits > 0)
            trie.BuildIndex(indexBits);
        return trie;
    }

    public static PooledTrie<TKey, IPNetwork, int> BuildMultiLevel<TKey>(RealData.Table<TKey> table, int rootBits, int subBits, int threshold, int width, bool report = false)
        where TKey : struct, IBitKey<TKey>
    {
        var trie = BuildPooled(table, 0);
        var (subTables, bytes) = trie.BuildMultiLevelIndex(rootBits, subBits, threshold, width);
        if (report)
            Console.WriteLine($"  {"",-26} multi-level {rootBits}/{subBits}/{threshold}: {subTables:N0} sub-tables, index {bytes / 1048576.0:F1} MB");
        return trie;
    }

    public static SnapshotTrie<TKey, IPNetwork, int> BuildSnapshot<TKey>(RealData.Table<TKey> table, int width, int threshold = 8, bool compact = true)
        where TKey : struct, IBitKey<TKey>
    {
        var trie = new SnapshotTrie<TKey, IPNetwork, int>(width, 16, 8, threshold);
        for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        if (compact)
            trie.Compact();
        return trie;
    }

    public static SnapshotTrie<TKey, IPNetwork, int> BulkSnapshot<TKey>(RealData.Table<TKey> table, int width, int threshold)
        where TKey : struct, IBitKey<TKey>
    {
        var trie = new SnapshotTrie<TKey, IPNetwork, int>(width, 16, 8, threshold);
        trie.BeginBulkLoad();
        for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        trie.EndBulkLoad();
        return trie;
    }

    /// <summary>Insert cost with the index kept current, the garbage that leaves, and what compaction costs and reclaims.</summary>
    public static void SnapshotCosts()
    {
        Costs("Real IPv4", RealData.V4, 32);
        Costs("Real IPv6", RealData.V6, 128);
    }

    private static void Costs<TKey>(string name, RealData.Table<TKey> table, int width) where TKey : struct, IBitKey<TKey>
    {
        foreach (var threshold in new[] { 8, 32 })
        {
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            var trie = new SnapshotTrie<TKey, IPNetwork, int>(width, 16, 8, threshold);
            for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
                trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
            var insert = stopwatch.Elapsed.TotalMilliseconds;
            var garbage = trie.Garbage;
            var before = trie.MemoryInBytes();
            stopwatch.Restart();
            trie.Compact();
            var compact = stopwatch.Elapsed.TotalMilliseconds;
            var after = trie.MemoryInBytes();

            stopwatch.Restart();
            for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex += 100)
                trie.TryRemove(in table.Bits[prefixIndex], table.Lengths[prefixIndex]);
            for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex += 100)
                trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
            var churnOperations = 2 * ((table.Bits.Length + 99) / 100);
            var churn = stopwatch.Elapsed.TotalMilliseconds;

            GC.Collect();
            stopwatch.Restart();
            var bulk = new SnapshotTrie<TKey, IPNetwork, int>(width, 16, 8, threshold);
            bulk.BeginBulkLoad();
            for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
                bulk.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
            var bulkInsert = stopwatch.Elapsed.TotalMilliseconds;
            bulk.EndBulkLoad();
            var bulkTotal = stopwatch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"{name}, threshold {threshold}: bulk load {bulkInsert:F0} ms inserts + {bulkTotal - bulkInsert:F0} ms index = {bulkTotal:F0} ms, memory {bulk.MemoryInBytes() / 1048576.0:F1} MB");

            Console.WriteLine($"{name}, threshold {threshold}: insert all {insert:F0} ms ({insert * 1000 / table.Bits.Length:F2} us each), " +
                              $"garbage sub-table entries {garbage.SubEntries:N0}, memory {before / 1048576.0:F1} MB -> compact {compact:F0} ms -> {after / 1048576.0:F1} MB; " +
                              $"then {churnOperations:N0} removes and re-adds: {churn * 1000 / churnOperations:F2} us each");
        }
    }

    public static PooledTrie<TKey, IPNetwork, int> BuildLeafPushed<TKey>(RealData.Table<TKey> table, int rootBits, int subBits, int width, bool report = false)
        where TKey : struct, IBitKey<TKey>
    {
        var trie = BuildPooled(table, 0);
        var stopwatch = Stopwatch.StartNew();
        var (tables, bytes) = trie.BuildLeafPushed(rootBits, subBits, width);
        if (report)
            Console.WriteLine($"  {"",-26} leaf-pushed {rootBits}/{subBits}: {tables:N0} child tables, tables {bytes / 1048576.0:F1} MB, built in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        return trie;
    }

    public static PooledTrie<TKey, IPNetwork, int> BuildCompressed<TKey>(RealData.Table<TKey> table, int rootBits, int width, bool report = false)
        where TKey : struct, IBitKey<TKey>
    {
        var trie = BuildPooled(table, 0);
        var stopwatch = Stopwatch.StartNew();
        var (nodes, bytes) = trie.BuildCompressed(rootBits, width);
        if (report)
            Console.WriteLine($"  {"",-26} compressed {rootBits}/6: {nodes:N0} nodes, tables {bytes / 1048576.0:F1} MB, built in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        return trie;
    }

    private static double AverageTableReads<TKey>(RealData.Table<TKey> table, PooledTrie<TKey, IPNetwork, int> trie) where TKey : struct, IBitKey<TKey>
    {
        long reads = 0;
        for (var keyIndex = 0; keyIndex < table.Keys.Length; keyIndex++)
            reads += trie.CountTableReads(in table.Keys[keyIndex]);
        return reads / (double)table.Keys.Length;
    }

    public static NetworkPrefixLookup<int> BuildCurrent<TKey>(RealData.Table<TKey> table) where TKey : struct, IBitKey<TKey>
    {
        var lookup = new NetworkPrefixLookup<int>();
        for (var prefixIndex = 0; prefixIndex < table.Networks.Length; prefixIndex++)
            lookup.TryAdd(new NetworkPrefix(table.Networks[prefixIndex].BaseAddress, table.Networks[prefixIndex].PrefixLength), table.Asns[prefixIndex]);
        return lookup;
    }

    public static void Verify()
    {
        VerifyTable("IPv4", RealData.V4, new[] { 0, 16, 18, 20 }, 32);
        VerifyTable("IPv6", RealData.V6, new[] { 0, 16, 20 }, 128);
    }

    private static void VerifyTable<TKey>(string name, RealData.Table<TKey> table, int[] indexSizes, int width) where TKey : struct, IBitKey<TKey>
    {
        Console.WriteLine($"{name}: {table.Bits.Length:N0} prefixes, {table.Keys.Length:N0} keys");
        var current = BuildCurrent(table);
        var expected = new string?[table.Keys.Length];
        for (var keyIndex = 0; keyIndex < expected.Length; keyIndex++)
            expected[keyIndex] = current.TryGetMatch(table.Addresses[keyIndex], out var match) ? $"{match.Prefix.Address}/{match.Prefix.Length}" : null;

        void Check(string variant, Func<int, IPNetwork?> lookup)
        {
            var mismatches = 0;
            for (var keyIndex = 0; keyIndex < expected.Length; keyIndex++)
            {
                var network = lookup(keyIndex);
                var text = network == null ? null : $"{network.Value.BaseAddress}/{network.Value.PrefixLength}";
                if (text != expected[keyIndex] && mismatches++ < 3)
                    Console.WriteLine($"    mismatch at {table.Addresses[keyIndex]}: expected {expected[keyIndex]}, got {text}");
            }
            Console.WriteLine($"  {variant,-26} mismatches: {mismatches}");
        }

        var cached = BuildCached(table);
        Check("cached (baseline)", keyIndex => cached.TryGetMatch(in table.Keys[keyIndex], out var prefix, out _) ? prefix : null);
        foreach (var bits in indexSizes)
        {
            var pooled = BuildPooled(table, bits);
            Check(bits == 0 ? "pooled, depth-first" : $"pooled, index {bits}", keyIndex => pooled.TryGetMatch(in table.Keys[keyIndex], out var prefix, out _) ? prefix : null);
            long visited = 0;
            for (var keyIndex = 0; keyIndex < table.Keys.Length; keyIndex++)
                visited += pooled.CountVisited(in table.Keys[keyIndex]);
            Console.WriteLine($"  {"",-26} nodes: {pooled.NodeCount:N0}, average nodes visited: {visited / (double)table.Keys.Length:F2}");
        }

        foreach (var (rootBits, subBits) in width == 32 ? new[] { (16, 8), (20, 4), (24, 8) } : new[] { (16, 8), (20, 8) })
        {
            var leafPushed = BuildLeafPushed(table, rootBits, subBits, width, report: true);
            Check($"leaf-pushed {rootBits}/{subBits}", keyIndex => leafPushed.TryGetMatch(in table.Keys[keyIndex], out var prefix, out _) ? prefix : null);
            Console.WriteLine($"  {"",-26} average table reads: {AverageTableReads(table, leafPushed):F2}");
        }

        foreach (var rootBits in new[] { 16, 18, 20 })
        {
            var compressed = BuildCompressed(table, rootBits, width, report: true);
            Check($"compressed {rootBits}/6", keyIndex => compressed.TryGetMatch(in table.Keys[keyIndex], out var prefix, out _) ? prefix : null);
            Console.WriteLine($"  {"",-26} average table reads, including the answer: {AverageTableReads(table, compressed):F2}");
        }

        foreach (var (rootBits, subBits, threshold) in new[] { (16, 8, 8), (16, 8, 32), (16, 4, 4), (16, 4, 16) })
        {
            var multiLevel = BuildMultiLevel(table, rootBits, subBits, threshold, width, report: true);
            Check($"multi-level {rootBits}/{subBits}/{threshold}", keyIndex => multiLevel.TryGetMatch(in table.Keys[keyIndex], out var prefix, out _) ? prefix : null);
            long visited = 0;
            for (var keyIndex = 0; keyIndex < table.Keys.Length; keyIndex++)
                visited += multiLevel.CountVisited(in table.Keys[keyIndex]);
            Console.WriteLine($"  {"",-26} average nodes visited: {visited / (double)table.Keys.Length:F2}");
        }
    }

    /// <summary>Quick timing on real data. Variants: v4-cached, v4-pooled, v4-index&lt;k&gt;, v6-cached, v6-pooled, v6-index&lt;k&gt;.</summary>
    public static void Time(string variant)
    {
        Func<long> run = variant switch
        {
            "v4-cached" => Single(RealData.V4, BuildCached(RealData.V4)),
            "v4-pooled" => Single(RealData.V4, BuildPooled(RealData.V4, 0)),
            _ when variant.StartsWith("v4-index") => Single(RealData.V4, BuildPooled(RealData.V4, int.Parse(variant[8..]))),
            "v6-cached" => Single(RealData.V6, BuildCached(RealData.V6)),
            "v6-pooled" => Single(RealData.V6, BuildPooled(RealData.V6, 0)),
            _ when variant.StartsWith("v6-index") => Single(RealData.V6, BuildPooled(RealData.V6, int.Parse(variant[8..]))),
            "v4-snap" => SingleSnapshot(RealData.V4, BuildSnapshot(RealData.V4, 32)),
            "v6-snap" => SingleSnapshot(RealData.V6, BuildSnapshot(RealData.V6, 128)),
            "v6-snap4" => SingleSnapshot(RealData.V6, BuildSnapshot(RealData.V6, 128, 4)),
            // churn: bulk load, remove a share of the prefixes and add them back in random order, optionally compact.
            // The contents end up identical, so any slowdown comes from layout and garbage. Example: v4-churn50, v6-churn50c
            _ when variant.StartsWith("v4-churn") => SingleSnapshot(RealData.V4, Churned(RealData.V4, 32, variant[8..])),
            _ when variant.StartsWith("v6-churn") => SingleSnapshot(RealData.V6, Churned(RealData.V6, 128, variant[8..])),
            // frozen tables and the multi-level index. A trailing 'i' times only finding the value index, without reading
            // the prefix and value; a trailing 'v' reads only the value, from a dense array of values.
            // Examples: v4-lp16-8, v4-lp16-8i, v6-cp16, v6-cp16v
            _ when variant.StartsWith("v4-lp") => Frozen(RealData.V4, LeafPushed(RealData.V4, Shape(variant), 32), variant[^1]),
            _ when variant.StartsWith("v6-lp") => Frozen(RealData.V6, LeafPushed(RealData.V6, Shape(variant), 128), variant[^1]),
            _ when variant.StartsWith("v4-cp") => Frozen(RealData.V4, BuildCompressed(RealData.V4, int.Parse(Shape(variant)), 32), variant[^1]),
            _ when variant.StartsWith("v6-cp") => Frozen(RealData.V6, BuildCompressed(RealData.V6, int.Parse(Shape(variant)), 128), variant[^1]),
            _ when variant.StartsWith("v4-ml") => Frozen(RealData.V4, MultiLevel(RealData.V4, Shape(variant), 32), variant[^1]),
            _ when variant.StartsWith("v6-ml") => Frozen(RealData.V6, MultiLevel(RealData.V6, Shape(variant), 128), variant[^1]),
            _ => throw new ArgumentException(variant)
        };

        for (var warmupRun = 0; warmupRun < 5; warmupRun++)
            run();
        var best = double.MaxValue;
        for (var timedRun = 0; timedRun < 15; timedRun++)
        {
            var stopwatch = Stopwatch.StartNew();
            run();
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"{variant,-16} best of 15: {best,7:F1} ms per 1M lookups");
    }

    private static SnapshotTrie<TKey, IPNetwork, int> Churned<TKey>(RealData.Table<TKey> table, int width, string spec) where TKey : struct, IBitKey<TKey>
    {
        var compact = spec.EndsWith('c');
        var percent = int.Parse(compact ? spec[..^1] : spec);
        var trie = BulkSnapshot(table, width, 8);
        var memoryFresh = trie.MemoryInBytes();

        var random = new Random(5);
        var chosen = Enumerable.Range(0, table.Bits.Length).Where(_ => random.Next(100) < percent).OrderBy(_ => random.Next()).ToArray();
        var stopwatch = Stopwatch.StartNew();
        foreach (var prefixIndex in chosen)
            trie.TryRemove(in table.Bits[prefixIndex], table.Lengths[prefixIndex]);
        foreach (var prefixIndex in chosen.OrderBy(_ => random.Next()))
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        var churnMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        var garbage = trie.Garbage;
        var memoryChurned = trie.MemoryInBytes();
        if (compact)
            trie.Compact();

        Console.Error.WriteLine($"{percent}% churn: {chosen.Length * 2:N0} changes in {churnMilliseconds:F0} ms; garbage nodes {garbage.Nodes:N0}, values {garbage.Values:N0}, sub-table entries {garbage.SubEntries:N0}; " +
                                $"memory {memoryFresh / 1048576.0:F0} MB fresh, {memoryChurned / 1048576.0:F0} MB churned{(compact ? $", {trie.MemoryInBytes() / 1048576.0:F0} MB compacted" : "")}");
        return trie;
    }

    /// <summary>
    ///     Worst-case latency on the real IPv4 table: one writer makes a million changes as fast as it can while one
    ///     reader times every lookup. Runs once with an idle writer as a control for timer and scheduling noise, once
    ///     without compaction, and once compacting whenever garbage values exceed live ones. Reports write and lookup
    ///     percentiles, garbage collections, and the slowest writes with what they did.
    /// </summary>
    public static void TailLatency()
    {
        TailLatency(RealData.V4, 32, compact: false, changes: 0);
        foreach (var compact in new[] { false, true })
            TailLatency(RealData.V4, 32, compact, 1_000_000);
    }

    private static void TailLatency<TKey>(RealData.Table<TKey> table, int width, bool compact, int changes) where TKey : struct, IBitKey<TKey>
    {
        var trie = BulkSnapshot(table, width, 8);
        var writeNanoseconds = new long[changes];
        var slowest = new List<(long Nanoseconds, string What)>();
        var readHistogram = new long[LatencyBuckets];
        long readMaximum = 0, reads = 0;
        var stop = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var collectionsBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var pauseBefore = GC.GetTotalPauseDuration();

        var reader = new Thread(() =>
        {
            var keys = table.Keys;
            var keyIndex = 0;
            while (Volatile.Read(ref stop) == 0)
            {
                var startTimestamp = Stopwatch.GetTimestamp();
                trie.TryGetMatch(in keys[keyIndex], out _, out _);
                var nanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - startTimestamp);
                readHistogram[LatencyBucket(nanoseconds)]++;
                if (nanoseconds > readMaximum)
                    readMaximum = nanoseconds;
                reads++;
                if (++keyIndex == keys.Length)
                    keyIndex = 0;
            }
        });
        reader.Start();

        var random = new Random(9);
        var removed = new HashSet<int>();
        int compactions = 0, growths = 0;
        if (changes == 0)
            Thread.Sleep(TimeSpan.FromSeconds(6)); // about as long as the runs with writes
        for (var changeIndex = 0; changeIndex < changes; changeIndex++)
        {
            var prefixIndex = random.Next(table.Bits.Length);
            var memory = trie.MemoryInBytes();
            var adding = removed.Remove(prefixIndex);
            var startTimestamp = Stopwatch.GetTimestamp();
            if (adding)
                trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
            else
                trie.TryRemove(in table.Bits[prefixIndex], table.Lengths[prefixIndex]);
            var compacted = compact && trie.Garbage.Values > trie.Count;
            if (compacted)
                trie.Compact();
            writeNanoseconds[changeIndex] = ToNanoseconds(Stopwatch.GetTimestamp() - startTimestamp);
            if (!adding)
                removed.Add(prefixIndex);

            var grew = !compacted && trie.MemoryInBytes() != memory;
            compactions += compacted ? 1 : 0;
            growths += grew ? 1 : 0;
            if (slowest.Count < 8 || writeNanoseconds[changeIndex] > slowest[^1].Nanoseconds)
            {
                var what = $"{(adding ? "add" : "remove")} /{table.Lengths[prefixIndex]}{(grew ? ", arrays grew" : "")}{(compacted ? ", compacted" : "")}";
                slowest.Add((writeNanoseconds[changeIndex], what));
                slowest.Sort((first, second) => second.Nanoseconds.CompareTo(first.Nanoseconds));
                if (slowest.Count > 8)
                    slowest.RemoveAt(8);
            }
        }

        Volatile.Write(ref stop, 1);
        reader.Join();
        var pause = GC.GetTotalPauseDuration() - pauseBefore;
        var collections = (GC.CollectionCount(0) - collectionsBefore.Item1, GC.CollectionCount(1) - collectionsBefore.Item2, GC.CollectionCount(2) - collectionsBefore.Item3);

        Array.Sort(writeNanoseconds);
        string W(double quantile) => Format(writeNanoseconds[(int)Math.Min(writeNanoseconds.Length - 1, quantile * writeNanoseconds.Length)]);
        string R(double quantile) => Format(Percentile(readHistogram, reads, quantile));
        var mode = changes == 0 ? "idle writer" : compact ? "compacting when garbage exceeds live values" : "never compacting";
        Console.WriteLine($"{mode}: {changes:N0} changes, {growths} growths, {compactions} compactions, memory at end {trie.MemoryInBytes() / 1048576.0:F0} MB");
        if (changes > 0)
            Console.WriteLine($"  writes:  p50 {W(0.5)}, p99 {W(0.99)}, p99.9 {W(0.999)}, p99.99 {W(0.9999)}, max {Format(writeNanoseconds[^1])}, total {writeNanoseconds.Sum() / 1e9:F1} s");
        Console.WriteLine($"  lookups: p50 {R(0.5)}, p99 {R(0.99)}, p99.9 {R(0.999)}, p99.99 {R(0.9999)}, max {Format(readMaximum)}, {reads:N0} lookups");
        Console.WriteLine($"  garbage collections: {collections.Item1} gen0, {collections.Item2} gen1, {collections.Item3} gen2, {pause.TotalMilliseconds:F0} ms paused in total");
        Console.WriteLine("  slowest writes: " + string.Join("; ", slowest.Select(write => $"{Format(write.Nanoseconds)} {write.What}")));
    }

    // lookup latencies: 10 ns buckets up to 10 us, then buckets 5% apart
    private const int LatencyBuckets = 1400;

    private static long ToNanoseconds(long ticks) => ticks * 1_000_000_000L / Stopwatch.Frequency;

    private static int LatencyBucket(long nanoseconds) =>
        nanoseconds < 10_000 ? (int)(nanoseconds / 10) : Math.Min(LatencyBuckets - 1, 1000 + (int)(Math.Log(nanoseconds / 10_000.0) / Math.Log(1.05)));

    private static long BucketUpperBound(int bucket) =>
        bucket < 1000 ? (bucket + 1) * 10L : (long)(10_000 * Math.Pow(1.05, bucket - 1000 + 1));

    private static long Percentile(long[] histogram, long total, double quantile)
    {
        var target = (long)Math.Ceiling(quantile * total);
        long seen = 0;
        for (var bucket = 0; bucket < histogram.Length; bucket++)
            if ((seen += histogram[bucket]) >= target)
                return BucketUpperBound(bucket);
        return BucketUpperBound(histogram.Length - 1);
    }

    private static string Format(long nanoseconds) =>
        nanoseconds < 10_000 ? $"{nanoseconds:N0} ns" : nanoseconds < 10_000_000 ? $"{nanoseconds / 1000.0:N0} us" : $"{nanoseconds / 1e6:N0} ms";

    /// <summary>
    ///     Reader throughput on the real IPv4 table with three reader threads for 10 seconds, while the writer is idle,
    ///     makes continuous updates, or makes updates and compacts every 50,000 changes.
    /// </summary>
    public static void Interference()
    {
        var table = RealData.V4;
        foreach (var mode in new[] { "idle writer", "continuous updates", "updates and compaction", "idle writer again" })
        {
            var trie = BulkSnapshot(table, 32, 8);
            var stop = 0;
            long writes = 0, compactions = 0;
            var counts = new long[3];

            var writer = new Thread(() =>
            {
                if (mode.StartsWith("idle"))
                    return;
                var random = new Random(9);
                var removed = new HashSet<int>();
                while (Volatile.Read(ref stop) == 0)
                {
                    var prefixIndex = random.Next(table.Bits.Length);
                    if (removed.Remove(prefixIndex))
                        trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
                    else { removed.Add(prefixIndex); trie.TryRemove(in table.Bits[prefixIndex], table.Lengths[prefixIndex]); }
                    if (++writes % 50_000 == 0 && mode == "updates and compaction") { trie.Compact(); compactions++; }
                }
            });

            var readers = Enumerable.Range(0, 3).Select(readerIndex => new Thread(() =>
            {
                var keys = table.Keys;
                long lookups = 0;
                var keyIndex = readerIndex * 333_333;
                while (Volatile.Read(ref stop) == 0)
                {
                    for (var lookupInBatch = 0; lookupInBatch < 1000; lookupInBatch++)
                    {
                        trie.TryGetMatch(in keys[keyIndex], out _, out _);
                        if (++keyIndex == keys.Length)
                            keyIndex = 0;
                    }
                    lookups += 1000;
                }
                counts[readerIndex] = lookups;
            })).ToArray();

            writer.Start();
            foreach (var readerThread in readers)
                readerThread.Start();
            Thread.Sleep(TimeSpan.FromSeconds(10));
            Volatile.Write(ref stop, 1);
            writer.Join();
            foreach (var readerThread in readers)
                readerThread.Join();

            var total = counts.Sum() / 10.0;
            Console.WriteLine($"{mode,-24} readers: {total / 1e6:F2} M lookups/s in total ({total / 3e6:F2} M per thread); writer: {writes / 10.0:N0} changes/s, {compactions} compactions");
        }
    }

    // spec "16-8": root bits, child table bits
    private static PooledTrie<TKey, IPNetwork, int> LeafPushed<TKey>(RealData.Table<TKey> table, string spec, int width) where TKey : struct, IBitKey<TKey>
    {
        var parts = spec.Split('-').Select(int.Parse).ToArray();
        return BuildLeafPushed(table, parts[0], parts[1], width);
    }

    // spec "16-8-32": root bits, sub-table bits, threshold
    private static PooledTrie<TKey, IPNetwork, int> MultiLevel<TKey>(RealData.Table<TKey> table, string spec, int width) where TKey : struct, IBitKey<TKey>
    {
        var parts = spec.Split('-').Select(int.Parse).ToArray();
        return BuildMultiLevel(table, parts[0], parts[1], parts[2], width);
    }

    // "v4-cp18i" gives "18"
    private static string Shape(string variant) => variant[5..].TrimEnd('i', 'v');

    private static Func<long> Frozen<TKey>(RealData.Table<TKey> table, PooledTrie<TKey, IPNetwork, int> trie, char mode) where TKey : struct, IBitKey<TKey>
    {
        var keys = table.Keys;
        switch (mode)
        {
            case 'i':
                return () =>
                {
                    long sum = 0;
                    for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                        sum += trie.GetMatchIndex(in keys[keyIndex]);
                    return sum;
                };
            case 'v':
            {
                var values = Enumerable.Range(0, trie.ValueCount).Select(trie.GetValue).ToArray();
                return () =>
                {
                    long sum = 0;
                    for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                    {
                        var valueIndex = trie.GetMatchIndex(in keys[keyIndex]);
                        sum += valueIndex >= 0 ? values[valueIndex] : -1;
                    }
                    return sum;
                };
            }
            default:
                return Single(table, trie);
        }
    }

    private static Func<long> SingleSnapshot<TKey>(RealData.Table<TKey> table, SnapshotTrie<TKey, IPNetwork, int> trie) where TKey : struct, IBitKey<TKey>
    {
        var keys = table.Keys;
        return () =>
        {
            long sum = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                sum += trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return sum;
        };
    }

    private static Func<long> Single<TKey>(RealData.Table<TKey> table, CachedKeyTrie<TKey, IPNetwork, int> trie) where TKey : struct, IBitKey<TKey>
    {
        var keys = table.Keys;
        return () =>
        {
            long sum = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                sum += trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return sum;
        };
    }

    private static Func<long> Single<TKey>(RealData.Table<TKey> table, PooledTrie<TKey, IPNetwork, int> trie) where TKey : struct, IBitKey<TKey>
    {
        var keys = table.Keys;
        return () =>
        {
            long sum = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                sum += trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return sum;
        };
    }
}
