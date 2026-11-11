/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Diagnostics;
using System.Net;
using BenchmarkDotNet.Running;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

namespace PerformanceTest;

public static class Program
{
    public static void Main(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--memory")
        {
            Memory();
            return;
        }

        if (arguments.Length > 0 && arguments[0] == "--memory-comparison")
        {
            ComparisonMemory();
            return;
        }

        // pass the command line through so BenchmarkDotNet options such as --filter work; with no filter, run
        // everything, leaving out the real tables when ROUTEVIEWS_DIR is not set
        var types = RouteViews.Available
            ? new[] { typeof(RandomTableBenchmarks), typeof(MaxMindBenchmarks), typeof(RealTableBenchmarks), typeof(RealBuildBenchmarks), typeof(LayerBenchmarks), typeof(WriteBenchmarks), typeof(FrozenRootBenchmarks), typeof(ComparisonBenchmarks), typeof(RealNetworkQueryBenchmarks), typeof(RealMaxMindBenchmarks) }
            : new[] { typeof(RandomTableBenchmarks), typeof(MaxMindBenchmarks), typeof(FrozenRootBenchmarks), typeof(ComparisonBenchmarks) };
        if (arguments.Length == 0)
            arguments = new[] { "--filter", "*" };
        BenchmarkSwitcher.FromTypes(types).Run(arguments);
    }

    /// <summary>The tables of <see cref="ComparisonData" />, reported like PerformanceTest.V2's --memory.</summary>
    private static void ComparisonMemory()
    {
        foreach (var name in ComparisonData.Names)
        {
            var data = ComparisonData.Get(name);
            var networks = ComparisonBenchmarks.Networks(data);
            var before = ComparisonData.HeapBytes();
            var stopwatch = Stopwatch.StartNew();
            var lookup = new IPNetworkLookup<int>();
            for (var networkIndex = 0; networkIndex < networks.Length; networkIndex++)
                lookup.TryAdd(networks[networkIndex], data.Values[networkIndex]);
            var seconds = stopwatch.Elapsed.TotalSeconds;
            var held = ComparisonData.HeapBytes() - before;
            var frozen = lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed });
            var frozenHeld = ComparisonData.HeapBytes() - before - held;
            var compressed = lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed }, new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed });
            var compressedHeld = ComparisonData.HeapBytes() - before - held - frozenHeld;

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            foreach (var address in data.Addresses.Take(100_000))
                lookup.TryGetMatch(address, out _, out _);
            var perLookup = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 100_000.0;
            Console.WriteLine($"{name,-10} v3: {held / 1048576.0,8:F1} MB held, built in {seconds:F2} s, {perLookup:F0} bytes allocated per lookup; " +
                              $"frozen {frozenHeld / 1048576.0:F1} MB, compressed {compressedHeld / 1048576.0:F1} MB");
            GC.KeepAlive(lookup);
            GC.KeepAlive(frozen);
            GC.KeepAlive(compressed);
        }
    }

    /// <summary>Memory held by each form on the real tables, and bytes allocated per lookup.</summary>
    private static void Memory()
    {
        void Report(string name, Func<object> build)
        {
            var before = ComparisonData.HeapBytes();
            var stopwatch = Stopwatch.StartNew();
            var built = build();
            var seconds = stopwatch.Elapsed.TotalSeconds;
            var after = ComparisonData.HeapBytes();
            Console.WriteLine($"{name,-44} {(after - before) / 1048576.0,8:F1} MB {seconds,8:F2} s");
            GC.KeepAlive(built);
        }

        var randomNetworks = RandomTableBenchmarks.CreateNetworks();
        var addresses = RandomTableBenchmarks.CreateAddresses();
        var randomLookup = new IPNetworkLookup<string>(randomNetworks.Select(network => KeyValuePair.Create(network, string.Empty)));
        var frozenRandom = randomLookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed });
        Console.WriteLine($"random table, {randomLookup.Count:N0} prefixes: read-write {randomLookup.GetStatistics().IPv4.Bytes / 1048576.0:F1} MB, " +
                          $"frozen leaf-pushed {frozenRandom.Bytes / 1048576.0:F1} MB, frozen compressed {randomLookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed }).Bytes / 1048576.0:F1} MB");
        foreach (var (name, lookup) in new (string, IReadOnlyIPPrefixLookup<IPNetwork, string>)[] { ("read-write", randomLookup), ("frozen", frozenRandom) })
        {
            lookup.TryGetMatch(addresses[0], out _, out _);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            foreach (var address in addresses)
                lookup.TryGetMatch(address, out _, out _);
            Console.WriteLine($"bytes allocated per TryGetMatch(IPAddress), {name}: {(GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)addresses.Count:F1}");
        }

        if (!RouteViews.Available)
        {
            Console.WriteLine("Set ROUTEVIEWS_DIR for the real tables.");
            return;
        }

        var ipv4Table = RouteViews.IPv4;
        var ipv6Table = RouteViews.IPv6;
        IPNetworkLookup<int>? ipv4Lookup = null, ipv6Lookup = null;
        Report("IPv4 read-write, built as one batch", () => ipv4Lookup = RouteViews.Build(ipv4Table));
        Report("IPv4 frozen, leaf-pushed 16/8", () => ipv4Lookup!.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed }));
        Report("IPv4 frozen, compressed 18", () => ipv4Lookup!.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed }));
        Report("IPv6 read-write, built as one batch", () => ipv6Lookup = RouteViews.Build(ipv6Table));
        Report("IPv6 frozen, leaf-pushed 16/8", () => ipv6Lookup!.Freeze(new FrozenBitTrieOptions()));
        Report("IPv6 frozen, compressed 18", () => ipv6Lookup!.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed }));
        Console.WriteLine($"IPv4 statistics: {ipv4Lookup!.GetStatistics().IPv4}");
        Console.WriteLine($"IPv6 statistics: {ipv6Lookup!.GetStatistics().IPv6}");
        Console.WriteLine($"IPv4 frozen bytes: leaf-pushed {ipv4Lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed }).Bytes / 1048576.0:F1} MB, compressed {ipv4Lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed }).Bytes / 1048576.0:F1} MB");
        Console.WriteLine($"IPv6 frozen bytes: leaf-pushed {ipv6Lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.LeafPushed }).Bytes / 1048576.0:F1} MB, compressed {ipv6Lookup.Freeze(new FrozenBitTrieOptions { Layout = FrozenLayout.Compressed }).Bytes / 1048576.0:F1} MB");
    }
}
