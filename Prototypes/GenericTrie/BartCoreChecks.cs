/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024 Michael Crino

   This program is free software: you can redistribute it and/or modify
   it under the terms of the GNU Affero General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU Affero General Public License for more details.

   You should have received a copy of the GNU Affero General Public License
   along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Diagnostics;
using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace GenericTrie;

/// <summary>
///     Measurements of <see cref="BartTrie{TKey,TPrefix,TValue}" /> against <see cref="SnapshotTrie{TKey,TPrefix,TValue}" />
///     on the real tables: memory, and worst-case latency with work per write under churn. Lookup and build speed are in
///     <see cref="BartCoreBenchmarks" />.
/// </summary>
public static class BartCoreChecks
{
    public static BartTrie<TKey, IPNetwork, int> BuildBart<TKey>(RealData.Table<TKey> table, int width, bool bulk = true) where TKey : struct, IBitKey<TKey>
    {
        var trie = new BartTrie<TKey, IPNetwork, int>(width);
        if (bulk)
            trie.BeginBulkLoad();
        for (var prefixIndex = 0; prefixIndex < table.Bits.Length; prefixIndex++)
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        if (bulk)
            trie.EndBulkLoad();
        return trie;
    }

    public static void Memory()
    {
        GC.KeepAlive(RealData.V4);
        GC.KeepAlive(RealData.V6);
        MemoryOf("Real IPv4", RealData.V4, 32);
        MemoryOf("Real IPv6", RealData.V6, 128);
    }

    private static void MemoryOf<TKey>(string name, RealData.Table<TKey> table, int width) where TKey : struct, IBitKey<TKey>
    {
        var bulk = Retained(() => BuildBart(table, width));
        var live = Retained(() => BuildBart(table, width, bulk: false));
        var snapshot = Retained(() => RealDataChecks.BulkSnapshot(table, width, 8));

        // churn: remove and re-add half the prefixes in random order; the contents end up the same
        var trie = BuildBart(table, width);
        var random = new Random(5);
        var chosen = Enumerable.Range(0, table.Bits.Length).Where(_ => random.Next(2) == 0).OrderBy(_ => random.Next()).ToArray();
        var before = Heap();
        foreach (var prefixIndex in chosen)
            trie.TryRemove(in table.Bits[prefixIndex], table.Lengths[prefixIndex]);
        foreach (var prefixIndex in chosen.OrderBy(_ => random.Next()))
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        var growth = Heap() - before;
        GC.KeepAlive(trie);

        Console.WriteLine($"{name}: BartTrie {Mb(bulk)} bulk loaded, {Mb(live)} added one at a time, {Mb(growth)} growth after 50% churn; SnapshotTrie {Mb(snapshot)}");
    }

    private static long Heap()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(true);
    }

    private static long Retained(Func<object> build)
    {
        var before = Heap();
        var result = build();
        var after = Heap();
        GC.KeepAlive(result);
        return after - before;
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:F1} MB";

    /// <summary>
    ///     The tail-latency test of <see cref="RealDataChecks.TailLatency" /> for BartTrie: one writer makes a million
    ///     changes to the real IPv4 table as fast as it can while one reader times every lookup. Also counts the array
    ///     elements and nodes each write copies, which timer noise cannot distort.
    /// </summary>
    public static void TailLatency()
    {
        const int changes = 1_000_000;
        var table = RealData.V4;
        var trie = BuildBart(table, 32);
        var writeNanoseconds = new long[changes];
        var work = new long[changes];
        var slowest = new List<(long Nanoseconds, string What)>();
        var readHistogram = new long[1400];
        long readMax = 0, reads = 0;
        var stop = 0;

        var heapBefore = Heap();
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
                var nanoseconds = (Stopwatch.GetTimestamp() - startTimestamp) * 1_000_000_000L / Stopwatch.Frequency;
                readHistogram[Bucket(nanoseconds)]++;
                if (nanoseconds > readMax)
                    readMax = nanoseconds;
                reads++;
                if (++keyIndex == keys.Length)
                    keyIndex = 0;
            }
        });
        reader.Start();

        var random = new Random(9);
        var removed = new HashSet<int>(changes);
        trie.TakeWork();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var changeIndex = 0; changeIndex < changes; changeIndex++)
        {
            var prefixIndex = random.Next(table.Bits.Length);
            var adding = removed.Remove(prefixIndex);
            var startTimestamp = Stopwatch.GetTimestamp();
            if (adding)
                trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
            else
                trie.TryRemove(in table.Bits[prefixIndex], table.Lengths[prefixIndex]);
            writeNanoseconds[changeIndex] = (Stopwatch.GetTimestamp() - startTimestamp) * 1_000_000_000L / Stopwatch.Frequency;
            work[changeIndex] = trie.TakeWork();
            if (!adding)
                removed.Add(prefixIndex);
            if (slowest.Count < 6 || writeNanoseconds[changeIndex] > slowest[^1].Nanoseconds)
            {
                slowest.Add((writeNanoseconds[changeIndex], $"{(adding ? "add" : "remove")} /{table.Lengths[prefixIndex]}, {work[changeIndex]} copied"));
                slowest.Sort((first, second) => second.Nanoseconds.CompareTo(first.Nanoseconds));
                if (slowest.Count > 6)
                    slowest.RemoveAt(6);
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Volatile.Write(ref stop, 1);
        reader.Join();
        var pause = GC.GetTotalPauseDuration() - pauseBefore;
        var collections = (GC.CollectionCount(0) - collectionsBefore.Item1, GC.CollectionCount(1) - collectionsBefore.Item2, GC.CollectionCount(2) - collectionsBefore.Item3);
        foreach (var prefixIndex in removed)
            trie.TryAdd(in table.Bits[prefixIndex], table.Lengths[prefixIndex], table.Networks[prefixIndex], table.Asns[prefixIndex]);
        var heapGrowth = Heap() - heapBefore;

        var total = writeNanoseconds.Sum();
        Array.Sort(writeNanoseconds);
        Array.Sort(work);
        string WritePercentile(double quantile) => Format(writeNanoseconds[(int)Math.Min(writeNanoseconds.Length - 1, quantile * writeNanoseconds.Length)]);
        long WorkPercentile(double quantile) => work[(int)Math.Min(work.Length - 1, quantile * work.Length)];
        string ReadPercentile(double quantile) => Format(Percentile(readHistogram, reads, quantile));
        Console.WriteLine($"BartTrie, {changes:N0} changes to real IPv4, {total / 1e9:F1} s in total; heap growth after re-adding the removed prefixes: {Mb(heapGrowth)}");
        Console.WriteLine($"  writes:  p50 {WritePercentile(0.5)}, p99 {WritePercentile(0.99)}, p99.9 {WritePercentile(0.999)}, p99.99 {WritePercentile(0.9999)}, max {Format(writeNanoseconds[^1])}");
        Console.WriteLine($"  copied per write: median {WorkPercentile(0.5)}, p99 {WorkPercentile(0.99)}, p99.99 {WorkPercentile(0.9999)}, max {work[^1]} array elements and nodes");
        Console.WriteLine($"  lookups: p50 {ReadPercentile(0.5)}, p99 {ReadPercentile(0.99)}, p99.9 {ReadPercentile(0.999)}, p99.99 {ReadPercentile(0.9999)}, max {Format(readMax)}, {reads:N0} lookups");
        Console.WriteLine($"  garbage collections: {collections.Item1} gen0, {collections.Item2} gen1, {collections.Item3} gen2, {pause.TotalMilliseconds:F0} ms paused in total; " +
                          $"{allocated / (double)changes:N0} bytes allocated per write");
        Console.WriteLine("  slowest writes: " + string.Join("; ", slowest.Select(slowWrite => $"{Format(slowWrite.Nanoseconds)} {slowWrite.What}")));
    }

    /// <summary>
    ///     Quick timings, best of 15, of one million lookups from precomputed keys: SnapshotTrie and BartTrie on the real
    ///     IPv4 and IPv6 tables and on the random benchmark table. For steering optimizations; final numbers come from
    ///     <see cref="BartCoreBenchmarks" />.
    /// </summary>
    public static void Time()
    {
        var ipv4Table = RealData.V4;
        var ipv6Table = RealData.V6;
        var randomKeys = WidthData.Key32;
        var snapshotRandom = new SnapshotTrie<Bits32, int, int>(32);
        snapshotRandom.BeginBulkLoad();
        var bartRandom = new BartTrie<Bits32, int, int>(32);
        bartRandom.BeginBulkLoad();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix32.Length; prefixIndex++)
        {
            snapshotRandom.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], prefixIndex, prefixIndex);
            bartRandom.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], prefixIndex, prefixIndex);
        }
        snapshotRandom.EndBulkLoad();
        bartRandom.EndBulkLoad();

        var snapshot4 = RealDataChecks.BulkSnapshot(ipv4Table, 32, 8);
        var bart4 = BuildBart(ipv4Table, 32);
        var snapshot6 = RealDataChecks.BulkSnapshot(ipv6Table, 128, 8);
        var bart6 = BuildBart(ipv6Table, 128);

        Report("random, SnapshotTrie", () =>
        {
            long checksum = 0;
            foreach (var key in randomKeys)
                checksum += snapshotRandom.TryGetMatch(in key, out var prefix, out _) ? prefix : -1;
            return checksum;
        });
        Report("random, BartTrie", () =>
        {
            long checksum = 0;
            foreach (var key in randomKeys)
                checksum += bartRandom.TryGetMatch(in key, out var prefix, out _) ? prefix : -1;
            return checksum;
        });
        Report("IPv4, SnapshotTrie", () =>
        {
            long checksum = 0;
            foreach (var key in ipv4Table.Keys)
                checksum += snapshot4.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return checksum;
        });
        Report("IPv4, BartTrie", () =>
        {
            long checksum = 0;
            foreach (var key in ipv4Table.Keys)
                checksum += bart4.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return checksum;
        });
        Report("IPv6, SnapshotTrie", () =>
        {
            long checksum = 0;
            foreach (var key in ipv6Table.Keys)
                checksum += snapshot6.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return checksum;
        });
        Report("IPv6, BartTrie", () =>
        {
            long checksum = 0;
            foreach (var key in ipv6Table.Keys)
                checksum += bart6.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
            return checksum;
        });
    }

    private static void Report(string name, Func<long> run)
    {
        for (var warmup = 0; warmup < 5; warmup++)
            run();
        var best = double.MaxValue;
        for (var trial = 0; trial < 15; trial++)
        {
            var stopwatch = Stopwatch.StartNew();
            run();
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"  {name,-22} {best,7:F1} ms per 1M lookups");
    }

    private static int Bucket(long nanoseconds) => nanoseconds < 10_000 ? (int)(nanoseconds / 10) : Math.Min(1399, 1000 + (int)(Math.Log(nanoseconds / 10_000.0) / Math.Log(1.05)));

    private static long UpperBound(int bucket) => bucket < 1000 ? (bucket + 1) * 10L : (long)(10_000 * Math.Pow(1.05, bucket - 1000 + 1));

    private static long Percentile(long[] histogram, long total, double quantile)
    {
        var target = (long)Math.Ceiling(quantile * total);
        long seen = 0;
        for (var bucket = 0; bucket < histogram.Length; bucket++)
            if ((seen += histogram[bucket]) >= target)
                return UpperBound(bucket);
        return UpperBound(histogram.Length - 1);
    }

    private static string Format(long nanoseconds) => nanoseconds < 10_000 ? $"{nanoseconds:N0} ns" : nanoseconds < 10_000_000 ? $"{nanoseconds / 1000.0:N0} us" : $"{nanoseconds / 1e6:N0} ms";
}

/// <summary>
///     BartTrie against SnapshotTrie in one session: one million lookups from IPAddress objects on the real tables, one
///     million lookups from precomputed keys on the random benchmark table, which fits in cache, and building the real
///     tables. SnapshotTrie is the baseline in each category. Each table is built only in the process that measures it.
/// </summary>
[MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class BartCoreBenchmarks
{
    private static readonly Lazy<SnapshotTrie<Bits32, IPNetwork, int>> V4Snapshot = new(() => RealDataChecks.BulkSnapshot(RealData.V4, 32, 8));
    private static readonly Lazy<BartTrie<Bits32, IPNetwork, int>> V4Bart = new(() => BartCoreChecks.BuildBart(RealData.V4, 32));
    private static readonly Lazy<PooledTrie<Bits32, IPNetwork, int>> V4Frozen = new(() => RealDataChecks.BuildLeafPushed(RealData.V4, 16, 8, 32));
    private static readonly Lazy<SnapshotTrie<Bits128, IPNetwork, int>> V6Snapshot = new(() => RealDataChecks.BulkSnapshot(RealData.V6, 128, 8));
    private static readonly Lazy<BartTrie<Bits128, IPNetwork, int>> V6Bart = new(() => BartCoreChecks.BuildBart(RealData.V6, 128));
    private static readonly Lazy<PooledTrie<Bits128, IPNetwork, int>> V6Frozen = new(() => RealDataChecks.BuildLeafPushed(RealData.V6, 16, 8, 128));
    private static readonly Lazy<SnapshotTrie<Bits32, int, int>> RandomSnapshot = new(BuildRandomSnapshot);
    private static readonly Lazy<BartTrie<Bits32, int, int>> RandomBart = new(BuildRandomBart);

    private static SnapshotTrie<Bits32, int, int> BuildRandomSnapshot()
    {
        var trie = new SnapshotTrie<Bits32, int, int>(32, 16, 8, 8);
        trie.BeginBulkLoad();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix32.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], prefixIndex, prefixIndex);
        trie.EndBulkLoad();
        return trie;
    }

    private static BartTrie<Bits32, int, int> BuildRandomBart()
    {
        var trie = new BartTrie<Bits32, int, int>(32);
        trie.BeginBulkLoad();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix32.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], prefixIndex, prefixIndex);
        trie.EndBulkLoad();
        return trie;
    }

    [Benchmark(Baseline = true), IterationCount(20), BenchmarkCategory("IPv4 lookup")]
    public long IPv4_SnapshotTrie() { var trie = V4Snapshot.Value; long sum = 0; foreach (var address in RealData.V4.Addresses) { var key = new Bits32(Optimization.FromAddressProperty(address)); sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1; } return sum; }

    [Benchmark, IterationCount(20), BenchmarkCategory("IPv4 lookup")]
    public long IPv4_BartTrie() { var trie = V4Bart.Value; long sum = 0; foreach (var address in RealData.V4.Addresses) { var key = new Bits32(Optimization.FromAddressProperty(address)); sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1; } return sum; }

    [Benchmark, IterationCount(20), BenchmarkCategory("IPv4 lookup")]
    public long IPv4_Frozen() { var trie = V4Frozen.Value; long sum = 0; foreach (var address in RealData.V4.Addresses) { var key = new Bits32(Optimization.FromAddressProperty(address)); sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1; } return sum; }

    [Benchmark(Baseline = true), IterationCount(20), BenchmarkCategory("IPv6 lookup")]
    public long IPv6_SnapshotTrie() { var trie = V6Snapshot.Value; long sum = 0; foreach (var address in RealData.V6.Addresses) { var key = new Bits128(IPBits.V6(address)); sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1; } return sum; }

    [Benchmark, IterationCount(20), BenchmarkCategory("IPv6 lookup")]
    public long IPv6_BartTrie() { var trie = V6Bart.Value; long sum = 0; foreach (var address in RealData.V6.Addresses) { var key = new Bits128(IPBits.V6(address)); sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1; } return sum; }

    [Benchmark, IterationCount(20), BenchmarkCategory("IPv6 lookup")]
    public long IPv6_Frozen() { var trie = V6Frozen.Value; long sum = 0; foreach (var address in RealData.V6.Addresses) { var key = new Bits128(IPBits.V6(address)); sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1; } return sum; }

    [Benchmark(Baseline = true), IterationCount(20), BenchmarkCategory("Random table in cache")]
    public long Random_SnapshotTrie()
    {
        var trie = RandomSnapshot.Value;
        long sum = 0;
        foreach (var key in WidthData.Key32)
            sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix + value : -1;
        return sum;
    }

    [Benchmark, IterationCount(20), BenchmarkCategory("Random table in cache")]
    public long Random_BartTrie()
    {
        var trie = RandomBart.Value;
        long sum = 0;
        foreach (var key in WidthData.Key32)
            sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix + value : -1;
        return sum;
    }

    [Benchmark(Baseline = true), IterationCount(10), BenchmarkCategory("IPv4 build")]
    public object IPv4_Build_SnapshotTrie_BulkLoad() => RealDataChecks.BulkSnapshot(RealData.V4, 32, 8);

    [Benchmark, IterationCount(10), BenchmarkCategory("IPv4 build")]
    public object IPv4_Build_BartTrie_BulkLoad() => BartCoreChecks.BuildBart(RealData.V4, 32);

    [Benchmark, IterationCount(10), BenchmarkCategory("IPv4 build")]
    public object IPv4_Build_BartTrie_OneAtATime() => BartCoreChecks.BuildBart(RealData.V4, 32, bulk: false);

    [Benchmark(Baseline = true), IterationCount(10), BenchmarkCategory("IPv6 build")]
    public object IPv6_Build_SnapshotTrie_BulkLoad() => RealDataChecks.BulkSnapshot(RealData.V6, 128, 8);

    [Benchmark, IterationCount(10), BenchmarkCategory("IPv6 build")]
    public object IPv6_Build_BartTrie_BulkLoad() => BartCoreChecks.BuildBart(RealData.V6, 128);

    [Benchmark, IterationCount(10), BenchmarkCategory("IPv6 build")]
    public object IPv6_Build_BartTrie_OneAtATime() => BartCoreChecks.BuildBart(RealData.V6, 128, bulk: false);
}

/// <summary>
///     BartTrie lookup variants in one session, as separate jobs that differ only in the BART_PRUNE and BART_SLOTBEST
///     switches: the plain lookup, pruned backtracking, and pruned backtracking with the slot's precomputed match for
///     the first two levels. One million lookups from precomputed keys.
/// </summary>
[Config(typeof(BartVariantConfig))]
public class BartVariantBenchmarks
{
    private static readonly Lazy<BartTrie<Bits32, IPNetwork, int>> RealIPv4Trie = new(() => BartCoreChecks.BuildBart(RealData.V4, 32));
    private static readonly Lazy<BartTrie<Bits128, IPNetwork, int>> RealIPv6Trie = new(() => BartCoreChecks.BuildBart(RealData.V6, 128));
    private static readonly Lazy<BartTrie<Bits32, int, int>> RandomTableTrie = new(() =>
    {
        var trie = new BartTrie<Bits32, int, int>(32);
        trie.BeginBulkLoad();
        for (var prefixIndex = 0; prefixIndex < WidthData.Prefix32.Length; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], prefixIndex, prefixIndex);
        trie.EndBulkLoad();
        return trie;
    });

    [Benchmark]
    public long RandomTable()
    {
        var trie = RandomTableTrie.Value;
        long sum = 0;
        foreach (var key in WidthData.Key32)
            sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix + value : -1;
        return sum;
    }

    [Benchmark]
    public long RealIPv4()
    {
        var trie = RealIPv4Trie.Value;
        long sum = 0;
        foreach (var key in RealData.V4.Keys)
            sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
        return sum;
    }

    [Benchmark]
    public long RealIPv6()
    {
        var trie = RealIPv6Trie.Value;
        long sum = 0;
        foreach (var key in RealData.V6.Keys)
            sum += trie.TryGetMatch(in key, out var prefix, out var value) ? prefix.PrefixLength + value : -1;
        return sum;
    }
}

public class BartVariantConfig : BenchmarkDotNet.Configs.ManualConfig
{
    public BartVariantConfig()
    {
        BenchmarkDotNet.Jobs.Job Variant(string jobId, string prune, string slotBest) => BenchmarkDotNet.Jobs.Job.Default
            .WithIterationCount(20)
            .WithEnvironmentVariables(new BenchmarkDotNet.Jobs.EnvironmentVariable("BART_PRUNE", prune), new BenchmarkDotNet.Jobs.EnvironmentVariable("BART_SLOTBEST", slotBest))
            .WithId(jobId);

        AddJob(Variant("Plain", "0", "0").AsBaseline());
        AddJob(Variant("Pruned", "1", "0"));
        AddJob(Variant("PrunedSlotBest", "1", "1"));
    }
}
