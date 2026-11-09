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

using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Bitvantage.InternetProtocol;
using GenericTrie;

if (args.Length > 0 && args[0] == "verify") { Verify.Run(); return; }
if (args.Length > 0 && args[0] == "verify-width") { WidthData.Verify(); return; }
if (args.Length > 1 && args[0] == "isolate") { WidthData.Isolate(args[1]); return; }
if (args.Length > 0 && args[0] == "verify-shapes") { ResultShapeComparison.Verify(); return; }
if (args.Length > 0 && args[0] == "verify-opt") { Optimization.Verify(); return; }
if (args.Length > 1 && args[0] == "time") { Optimization.Time(args[1]); return; }
if (args.Length > 0 && args[0] == "verify-real") { RealDataChecks.Verify(); return; }
if (args.Length > 0 && args[0] == "memory") { MemoryComparison.Run(); return; }
if (args.Length > 0 && args[0] == "snapshot-costs") { RealDataChecks.SnapshotCosts(); return; }
if (args.Length > 0 && args[0] == "interference") { RealDataChecks.Interference(); return; }
if (args.Length > 0 && args[0] == "tail-latency") { RealDataChecks.TailLatency(); return; }
if (args.Length > 0 && args[0] == "bart") { BartComparison.Run(); return; }
if (args.Length > 0 && args[0] == "bart-memory") { BartCoreChecks.Memory(); return; }
if (args.Length > 0 && args[0] == "bart-tail") { BartCoreChecks.TailLatency(); return; }
if (args.Length > 0 && args[0] == "bart-time") { BartCoreChecks.Time(); return; }
if (args.Length > 1 && args[0] == "time-real") { RealDataChecks.Time(args[1]); return; }
BenchmarkSwitcher.FromTypes(new[] { typeof(Comparison), typeof(WidthComparison), typeof(CachedWidthComparison), typeof(Width192Comparison), typeof(ResultShapeComparison), typeof(LargeValueShapeComparison),
    typeof(OptimizationComparison), typeof(EndToEndComparison), typeof(BuildComparison),
    typeof(RealIPv4Lookup), typeof(RealIPv6Lookup), typeof(RealFrozenLookup), typeof(RealBartBenchmarks), typeof(BartCoreBenchmarks), typeof(BartVariantBenchmarks), typeof(RealBuild) }).Run(args);

public static class Data
{
    // identical generators and seeds to PerformanceTest/Program.cs
    public static readonly List<NetworkPrefix> Prefixes = Gen(11).Take(100_000).Distinct().ToList();
    public static readonly List<IPNetwork> IPNetworks = Prefixes.Select(prefix => new IPNetwork(prefix.Address, prefix.Length)).ToList();
    public static readonly List<IPAddress> Addresses = Addrs(12).Take(1_000_000).ToList();

    private static IEnumerable<NetworkPrefix> Gen(int seed) { var random = new Random(seed); while (true) { var bytes = BitConverter.GetBytes(random.NextInt64(0, 0xff_ff_ff_ffL + 1)).Take(4).ToArray(); yield return new NetworkPrefix(new IPAddress(bytes), (ushort)Math.Cbrt(random.Next(0, 33 * 33 * 33))); } }
    private static IEnumerable<IPAddress> Addrs(int seed) { var random = new Random(seed); while (true) { var bytes = BitConverter.GetBytes(random.NextInt64(0, 0xff_ff_ff_ffL + 1)).Take(4).ToArray(); yield return new IPAddress(bytes); } }
}

public static class Verify
{
    public static void Run()
    {
        long Retained(Func<object> build) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var before = GC.GetTotalMemory(true); var built = build(); GC.Collect(); var after = GC.GetTotalMemory(true); GC.KeepAlive(built); return after - before; }
        var prefixCount = Data.Prefixes.Count;

        var current = new NetworkPrefixLookup<string>();
        foreach (var prefix in Data.Prefixes)
            current.TryAdd(prefix, "");
        var control = new UInt128Control<string>();
        foreach (var prefix in Data.Prefixes)
            control.TryAdd(prefix, "");
        var genericNetworkPrefix = new IPPrefixLookup<NetworkPrefix, NetworkPrefixAdapter, string>();
        foreach (var prefix in Data.Prefixes)
            genericNetworkPrefix.TryAdd(prefix, "");
        var genericIPNetwork = new IPPrefixLookup<IPNetwork, IPNetworkAdapter, string>();
        foreach (var network in Data.IPNetworks)
            genericIPNetwork.TryAdd(network, "");

        Console.WriteLine($"prefixes: {prefixCount:N0}");
        Console.WriteLine($"node counts (value/routing incl. root): current=96,944/75,986 (measured earlier)  control={control.V4.CountNodes()}  generic-NetworkPrefix={genericNetworkPrefix.V4.CountNodes()}  generic-IPNetwork={genericIPNetwork.V4.CountNodes()}");
        Console.WriteLine($"Count property: current={current.Count:N0} (known undercount)  generic={genericNetworkPrefix.V4.Count:N0}");

        int mismatches = 0, matched = 0;
        foreach (var address in Data.Addresses)
        {
            var hasCurrent = current.TryGetMatch(address, out var currentMatch);
            var hasControl = control.TryGetMatch(address, out var controlMatch, out _);
            var hasNetworkPrefix = genericNetworkPrefix.TryGetMatch(address, out var networkPrefixMatch, out _);
            var hasIPNetwork = genericIPNetwork.TryGetMatch(address, out var ipNetworkMatch, out _);
            if (hasCurrent)
                matched++;
            var currentText = hasCurrent ? currentMatch!.Prefix.ToString() : "-";
            if ((hasControl ? controlMatch.ToString() : "-") != currentText || (hasNetworkPrefix ? networkPrefixMatch.ToString() : "-") != currentText || (hasIPNetwork ? ipNetworkMatch.ToString() : "-") != currentText)
                if (mismatches++ < 5)
                    Console.WriteLine($"  MISMATCH {address}: current={currentText} control={controlMatch} np={networkPrefixMatch} ip={ipNetworkMatch}");
        }
        Console.WriteLine($"lookups compared: {Data.Addresses.Count:N0}, matched: {matched:N0}, mismatches: {mismatches}");

        Console.WriteLine($"retained bytes per prefix: current={Retained(() =>
                          {
                              var lookup = new NetworkPrefixLookup<string>();
                              foreach (var prefix in Data.Prefixes)
                                  lookup.TryAdd(prefix, "");
                              return lookup;
                          }) / (double)prefixCount:F0}" +
                          $"  control={Retained(() =>
                          {
                              var lookup = new UInt128Control<string>();
                              foreach (var prefix in Data.Prefixes)
                                  lookup.TryAdd(prefix, "");
                              return lookup;
                          }) / (double)prefixCount:F0}" +
                          $"  generic-NetworkPrefix={Retained(() =>
                          {
                              var lookup = new IPPrefixLookup<NetworkPrefix, NetworkPrefixAdapter, string>();
                              foreach (var prefix in Data.Prefixes)
                                  lookup.TryAdd(prefix, "");
                              return lookup;
                          }) / (double)prefixCount:F0}" +
                          $"  generic-IPNetwork={Retained(() =>
                          {
                              var lookup = new IPPrefixLookup<IPNetwork, IPNetworkAdapter, string>();
                              foreach (var network in Data.IPNetworks)
                                  lookup.TryAdd(network, "");
                              return lookup;
                          }) / (double)prefixCount:F0}");
        Console.WriteLine("  (excludes the NetworkPrefix and IPNetwork key objects, which exist before the build)");

        // IPv6 smoke test through the generic layer
        var ipv6Lookup = new IPPrefixLookup<IPNetwork, IPNetworkAdapter, string>();
        ipv6Lookup.TryAdd(IPNetwork.Parse("2001:db8::/32"), "a"); ipv6Lookup.TryAdd(IPNetwork.Parse("2001:db8:1::/48"), "b"); ipv6Lookup.TryAdd(IPNetwork.Parse("::/0"), "default");
        foreach (var addressText in new[] { "2001:db8:1::5", "2001:db8:2::5", "fe80::1" })
            Console.WriteLine($"  IPv6 {addressText} => {(ipv6Lookup.TryGetMatch(IPAddress.Parse(addressText), out var matchedPrefix, out var matchedValue) ? $"{matchedPrefix} {matchedValue}" : "none")}");
    }
}

[MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class Comparison
{
    private static readonly NetworkPrefixLookup<string> Current = new();
    private static readonly UInt128Control<string> Control = new();
    private static readonly IPPrefixLookup<NetworkPrefix, NetworkPrefixAdapter, string> GenericNetworkPrefix = new();
    private static readonly IPPrefixLookup<IPNetwork, IPNetworkAdapter, string> GenericIPNetwork = new();

    static Comparison()
    {
        foreach (var prefix in Data.Prefixes) { Current.TryAdd(prefix, string.Empty); Control.TryAdd(prefix, string.Empty); GenericNetworkPrefix.TryAdd(prefix, string.Empty); }
        foreach (var network in Data.IPNetworks)
            GenericIPNetwork.TryAdd(network, string.Empty);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Current_Lookup()
    {
        foreach (var address in Data.Addresses)
            if (Current.GetMatch(address).Value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Control128_Lookup()
    {
        foreach (var address in Data.Addresses)
            if (!Control.TryGetMatch(address, out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void GenericNetworkPrefix_Lookup()
    {
        foreach (var address in Data.Addresses)
            if (!GenericNetworkPrefix.TryGetMatch(address, out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void GenericIPNetwork_Lookup()
    {
        foreach (var address in Data.Addresses)
            if (!GenericIPNetwork.TryGetMatch(address, out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Current_Build()
    {
        var lookup = new NetworkPrefixLookup<string?>();
        foreach (var prefix in Data.Prefixes)
            lookup.TryAdd(prefix, default);
        return lookup;
    }

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Control128_Build()
    {
        var lookup = new UInt128Control<string?>();
        foreach (var prefix in Data.Prefixes)
            lookup.TryAdd(prefix, default);
        return lookup;
    }

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object GenericNetworkPrefix_Build()
    {
        var lookup = new IPPrefixLookup<NetworkPrefix, NetworkPrefixAdapter, string?>();
        foreach (var prefix in Data.Prefixes)
            lookup.TryAdd(prefix, default);
        return lookup;
    }

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object GenericIPNetwork_Build()
    {
        var lookup = new IPPrefixLookup<IPNetwork, IPNetworkAdapter, string?>();
        foreach (var network in Data.IPNetworks)
            lookup.TryAdd(network, default);
        return lookup;
    }
}

/// <summary>
///     Key width study. The same IPv4 prefixes and addresses are encoded as keys of different widths, so every trie has
///     the same shape and only the key representation differs. Keys are precomputed, so the timings measure the trie
///     walk alone, without IPAddress conversion.
/// </summary>
public static class WidthData
{
    // a fixed 192-bit context placed in front of the IPv4 bits for the worst-case 256-bit layout
    private const ulong Context0 = 0x0123456789ABCDEF, Context1 = 0xFEDCBA9876543210, Context2 = 0x0F1E2D3C4B5A6978;

    public static readonly int[] PrefixLengths = Data.Prefixes.Select(prefix => prefix.Length).ToArray();
    private static readonly uint[] PrefixBits = Data.Prefixes.Select(prefix => (uint)prefix.NetworkBits).ToArray();
    private static readonly uint[] AddressBits = Data.Addresses.Select(IPBits.V4).ToArray();

    public static readonly uint[] PrefixReference = PrefixBits;
    public static readonly Bits32[] Prefix32 = PrefixBits.Select(bits => new Bits32(bits)).ToArray();
    public static readonly Bits128[] Prefix128 = PrefixBits.Select(bits => new Bits128((UInt128)bits << 96)).ToArray();
    public static readonly Bits256[] Prefix256Front = PrefixBits.Select(Front).ToArray();
    public static readonly Bits256[] Prefix256Back = PrefixBits.Select(Back).ToArray();
    public static readonly BitsVar[] PrefixVarBack = PrefixBits.Select(VarBack).ToArray();
    public static readonly Bits192[] Prefix192Front = PrefixBits.Select(bits => new Bits192((ulong)bits << 32, 0, 0)).ToArray();
    public static readonly Bits192[] Prefix192Back = PrefixBits.Select(bits => new Bits192(Context0, Context1, (ulong)bits << 32)).ToArray();

    public static readonly uint[] KeyReference = AddressBits;
    public static readonly Bits32[] Key32 = AddressBits.Select(bits => new Bits32(bits)).ToArray();
    public static readonly Bits128[] Key128 = AddressBits.Select(bits => new Bits128((UInt128)bits << 96)).ToArray();
    public static readonly Bits256[] Key256Front = AddressBits.Select(Front).ToArray();
    public static readonly Bits256[] Key256Back = AddressBits.Select(Back).ToArray();
    public static readonly BitsVar[] KeyVarBack = AddressBits.Select(VarBack).ToArray();
    public static readonly Bits192[] Key192Front = AddressBits.Select(bits => new Bits192((ulong)bits << 32, 0, 0)).ToArray();
    public static readonly Bits192[] Key192Back = AddressBits.Select(bits => new Bits192(Context0, Context1, (ulong)bits << 32)).ToArray();

    private static Bits256 Front(uint bits) { var key = new Bits256(); key.Words[0] = (ulong)bits << 32; return key; }
    private static Bits256 Back(uint bits) { var key = new Bits256(); key.Words[0] = Context0; key.Words[1] = Context1; key.Words[2] = Context2; key.Words[3] = (ulong)bits << 32; return key; }
    private static BitsVar VarBack(uint bits) => new(new[] { Context0, Context1, Context2, (ulong)bits << 32 });

    public static BitTrie<uint, int, string?> BuildRef()
    {
        var trie = new BitTrie<uint, int, string?>();
        for (var prefixIndex = 0; prefixIndex < PrefixReference.Length; prefixIndex++)
            trie.TryAdd(PrefixReference[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static KeyTrie<Bits32, int, string?> Build32()
    {
        var trie = new KeyTrie<Bits32, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix32.Length; prefixIndex++)
            trie.TryAdd(in Prefix32[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static KeyTrie<Bits128, int, string?> Build128()
    {
        var trie = new KeyTrie<Bits128, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix128.Length; prefixIndex++)
            trie.TryAdd(in Prefix128[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static KeyTrie<Bits256, int, string?> Build256Front()
    {
        var trie = new KeyTrie<Bits256, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix256Front.Length; prefixIndex++)
            trie.TryAdd(in Prefix256Front[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static KeyTrie<Bits256, int, string?> Build256Back()
    {
        var trie = new KeyTrie<Bits256, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix256Back.Length; prefixIndex++)
            trie.TryAdd(in Prefix256Back[prefixIndex], 192 + PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static CachedKeyTrie<Bits32, int, string?> BuildC32()
    {
        var trie = new CachedKeyTrie<Bits32, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix32.Length; prefixIndex++)
            trie.TryAdd(in Prefix32[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static CachedKeyTrie<Bits128, int, string?> BuildC128()
    {
        var trie = new CachedKeyTrie<Bits128, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix128.Length; prefixIndex++)
            trie.TryAdd(in Prefix128[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static CachedKeyTrie<Bits256, int, string?> BuildC256Front()
    {
        var trie = new CachedKeyTrie<Bits256, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix256Front.Length; prefixIndex++)
            trie.TryAdd(in Prefix256Front[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static CachedKeyTrie<Bits256, int, string?> BuildC256Back()
    {
        var trie = new CachedKeyTrie<Bits256, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix256Back.Length; prefixIndex++)
            trie.TryAdd(in Prefix256Back[prefixIndex], 192 + PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    // the 192-bit worst case puts a 128-bit context in front, so every comparison covers all three words
    public static CachedKeyTrie<Bits192, int, string?> BuildC192Front()
    {
        var trie = new CachedKeyTrie<Bits192, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix192Front.Length; prefixIndex++)
            trie.TryAdd(in Prefix192Front[prefixIndex], PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static CachedKeyTrie<Bits192, int, string?> BuildC192Back()
    {
        var trie = new CachedKeyTrie<Bits192, int, string?>();
        for (var prefixIndex = 0; prefixIndex < Prefix192Back.Length; prefixIndex++)
            trie.TryAdd(in Prefix192Back[prefixIndex], 128 + PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static CachedKeyTrie<BitsVar, int, string?> BuildCVarBack()
    {
        var trie = new CachedKeyTrie<BitsVar, int, string?>();
        for (var prefixIndex = 0; prefixIndex < PrefixVarBack.Length; prefixIndex++)
            trie.TryAdd(in PrefixVarBack[prefixIndex], 192 + PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    public static KeyTrie<BitsVar, int, string?> BuildVarBack()
    {
        var trie = new KeyTrie<BitsVar, int, string?>();
        for (var prefixIndex = 0; prefixIndex < PrefixVarBack.Length; prefixIndex++)
            trie.TryAdd(in PrefixVarBack[prefixIndex], 192 + PrefixLengths[prefixIndex], prefixIndex, string.Empty);
        return trie;
    }

    /// <summary>
    ///     Builds only one trie in a fresh process and times 1M lookups, so heap layout is not affected by the other
    ///     variants. Prints the fastest of 15 timed runs after warm-up.
    /// </summary>
    public static void Isolate(string variant)
    {
        Func<int> run = variant switch
        {
            "ref32" => Make(BuildRef(), KeyReference, (trie, key) => trie.TryGetMatch(key, out var prefixIndex, out _) ? prefixIndex : -1),
            "key32" => Make(Build32(), Key32, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "key128" => Make(Build128(), Key128, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "key256front" => Make(Build256Front(), Key256Front, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "key256back" => Make(Build256Back(), Key256Back, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "keyvarback" => Make(BuildVarBack(), KeyVarBack, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cached32" => Make(BuildC32(), Key32, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cached128" => Make(BuildC128(), Key128, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cached256front" => Make(BuildC256Front(), Key256Front, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cached256back" => Make(BuildC256Back(), Key256Back, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cachedvarback" => Make(BuildCVarBack(), KeyVarBack, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cached192front" => Make(BuildC192Front(), Key192Front, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            "cached192back" => Make(BuildC192Back(), Key192Back, (trie, key) => trie.TryGetMatch(in key, out var prefixIndex, out _) ? prefixIndex : -1),
            _ => throw new ArgumentException(variant)
        };
        for (var warmupRun = 0; warmupRun < 5; warmupRun++)
            run();
        var best = double.MaxValue;
        for (var timedRun = 0; timedRun < 15; timedRun++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            run();
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"{variant,-12} best of 15: {best,7:F1} ms per 1M lookups");

        static Func<int> Make<TTrie, TKey>(TTrie trie, TKey[] keys, Func<TTrie, TKey, int> lookup) => () =>
        {
            var sum = 0;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                sum += lookup(trie, keys[keyIndex]);
            return sum;
        };
    }

    public static void Verify()
    {
        long Retained(Func<object> build) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var before = GC.GetTotalMemory(true); var built = build(); GC.Collect(); var after = GC.GetTotalMemory(true); GC.KeepAlive(built); return after - before; }
        double PerPrefix(long bytes) => bytes / (double)PrefixLengths.Length;

        var reference = BuildRef(); var keyTrie32 = Build32(); var keyTrie128 = Build128(); var keyTrie256Front = Build256Front(); var keyTrie256Back = Build256Back(); var keyTrieVarBack = BuildVarBack();
        Console.WriteLine($"nodes (value, routing incl. root): ref={reference.CountNodes()} 32={keyTrie32.CountNodes()} 128={keyTrie128.CountNodes()} 256front={keyTrie256Front.CountNodes()} 256back={keyTrie256Back.CountNodes()} varBack={keyTrieVarBack.CountNodes()}");

        int mismatches = 0;
        for (var keyIndex = 0; keyIndex < KeyReference.Length; keyIndex++)
        {
            reference.TryGetMatch(KeyReference[keyIndex], out var expected, out _);
            keyTrie32.TryGetMatch(in Key32[keyIndex], out var match32, out _);
            keyTrie128.TryGetMatch(in Key128[keyIndex], out var match128, out _);
            keyTrie256Front.TryGetMatch(in Key256Front[keyIndex], out var match256Front, out _);
            keyTrie256Back.TryGetMatch(in Key256Back[keyIndex], out var match256Back, out _);
            keyTrieVarBack.TryGetMatch(in KeyVarBack[keyIndex], out var matchVarBack, out _);
            if (match32 != expected || match128 != expected || match256Front != expected || match256Back != expected || matchVarBack != expected)
                if (mismatches++ < 5)
                    Console.WriteLine($"  MISMATCH key {keyIndex}: ref={expected} 32={match32} 128={match128} 256f={match256Front} 256b={match256Back} var={matchVarBack}");
        }
        Console.WriteLine($"lookups compared: {KeyReference.Length:N0}, mismatches: {mismatches}");

        var cachedTrie32 = BuildC32(); var cachedTrie128 = BuildC128(); var cachedTrie256Front = BuildC256Front(); var cachedTrie256Back = BuildC256Back(); var cachedTrieVarBack = BuildCVarBack();
        int cachedMismatches = 0;
        for (var keyIndex = 0; keyIndex < KeyReference.Length; keyIndex++)
        {
            reference.TryGetMatch(KeyReference[keyIndex], out var expected, out _);
            cachedTrie32.TryGetMatch(in Key32[keyIndex], out var match32, out _);
            cachedTrie128.TryGetMatch(in Key128[keyIndex], out var match128, out _);
            cachedTrie256Front.TryGetMatch(in Key256Front[keyIndex], out var match256Front, out _);
            cachedTrie256Back.TryGetMatch(in Key256Back[keyIndex], out var match256Back, out _);
            cachedTrieVarBack.TryGetMatch(in KeyVarBack[keyIndex], out var matchVarBack, out _);
            if (match32 != expected || match128 != expected || match256Front != expected || match256Back != expected || matchVarBack != expected)
                if (cachedMismatches++ < 5)
                    Console.WriteLine($"  CACHED MISMATCH key {keyIndex}: ref={expected} 32={match32} 128={match128} 256f={match256Front} 256b={match256Back} var={matchVarBack}");
        }
        Console.WriteLine($"cached-mask variants: lookups compared: {KeyReference.Length:N0}, mismatches: {cachedMismatches}");
        Console.WriteLine($"cached-mask retained bytes per prefix: 32={PerPrefix(Retained(BuildC32)):F0} 128={PerPrefix(Retained(BuildC128)):F0} 256={PerPrefix(Retained(BuildC256Back)):F0} var={PerPrefix(Retained(BuildCVarBack)):F0}");

        var cachedTrie192Front = BuildC192Front(); var cachedTrie192Back = BuildC192Back();
        int mismatches192 = 0;
        for (var keyIndex = 0; keyIndex < KeyReference.Length; keyIndex++)
        {
            reference.TryGetMatch(KeyReference[keyIndex], out var expected, out _);
            cachedTrie192Front.TryGetMatch(in Key192Front[keyIndex], out var matchFront, out _);
            cachedTrie192Back.TryGetMatch(in Key192Back[keyIndex], out var matchBack, out _);
            if (matchFront != expected || matchBack != expected)
                if (mismatches192++ < 5)
                    Console.WriteLine($"  192 MISMATCH key {keyIndex}: ref={expected} front={matchFront} back={matchBack}");
        }
        Console.WriteLine($"192-bit cached: lookups compared: {KeyReference.Length:N0}, mismatches: {mismatches192}, retained bytes per prefix: {PerPrefix(Retained(BuildC192Back)):F0}");

        Console.WriteLine($"retained bytes per prefix: ref={PerPrefix(Retained(BuildRef)):F0} 32={PerPrefix(Retained(Build32)):F0} 128={PerPrefix(Retained(Build128)):F0} " +
                          $"256front={PerPrefix(Retained(Build256Front)):F0} 256back={PerPrefix(Retained(Build256Back)):F0} varBack={PerPrefix(Retained(BuildVarBack)):F0}");
    }
}

[MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class WidthComparison
{
    private static readonly BitTrie<uint, int, string?> Reference = WidthData.BuildRef();
    private static readonly KeyTrie<Bits32, int, string?> Key32Trie = WidthData.Build32();
    private static readonly KeyTrie<Bits128, int, string?> Key128Trie = WidthData.Build128();
    private static readonly KeyTrie<Bits256, int, string?> Key256FrontTrie = WidthData.Build256Front();
    private static readonly KeyTrie<Bits256, int, string?> Key256BackTrie = WidthData.Build256Back();
    private static readonly KeyTrie<BitsVar, int, string?> KeyVarBackTrie = WidthData.BuildVarBack();

    [Benchmark(Baseline = true), BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Ref32_IBinaryInteger_Lookup()
    {
        var keys = WidthData.KeyReference;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Reference.TryGetMatch(keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Key32_Lookup()
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Key32Trie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Key128_Lookup()
    {
        var keys = WidthData.Key128;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Key128Trie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Key256Front_Lookup()
    {
        var keys = WidthData.Key256Front;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Key256FrontTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void Key256Back_Lookup()
    {
        var keys = WidthData.Key256Back;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Key256BackTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("Lookup 1M"), IterationCount(30)]
    public void KeyVarBack_Lookup()
    {
        var keys = WidthData.KeyVarBack;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!KeyVarBackTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Ref32_IBinaryInteger_Build() => WidthData.BuildRef();

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Key32_Build() => WidthData.Build32();

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Key128_Build() => WidthData.Build128();

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Key256Front_Build() => WidthData.Build256Front();

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object Key256Back_Build() => WidthData.Build256Back();

    [Benchmark, BenchmarkCategory("Build 97K"), IterationCount(30)]
    public object KeyVarBack_Build() => WidthData.BuildVarBack();
}

/// <summary>Lookups through <see cref="CachedKeyTrie{TKey,TPrefix,TValue}" />, with two references from WidthComparison.</summary>
[MemoryDiagnoser]
public class CachedWidthComparison
{
    private static readonly BitTrie<uint, int, string?> Reference = WidthData.BuildRef();
    private static readonly KeyTrie<Bits32, int, string?> Key32Trie = WidthData.Build32();
    private static readonly CachedKeyTrie<Bits32, int, string?> Cached32Trie = WidthData.BuildC32();
    private static readonly CachedKeyTrie<Bits128, int, string?> Cached128Trie = WidthData.BuildC128();
    private static readonly CachedKeyTrie<Bits256, int, string?> Cached256FrontTrie = WidthData.BuildC256Front();
    private static readonly CachedKeyTrie<Bits256, int, string?> Cached256BackTrie = WidthData.BuildC256Back();
    private static readonly CachedKeyTrie<BitsVar, int, string?> CachedVarBackTrie = WidthData.BuildCVarBack();

    [Benchmark(Baseline = true), IterationCount(30)]
    public void Ref32_IBinaryInteger_Lookup()
    {
        var keys = WidthData.KeyReference;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Reference.TryGetMatch(keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Key32_Computed_Lookup()
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Key32Trie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached32_Lookup()
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached32Trie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached128_Lookup()
    {
        var keys = WidthData.Key128;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached128Trie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached256Front_Lookup()
    {
        var keys = WidthData.Key256Front;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached256FrontTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached256Back_Lookup()
    {
        var keys = WidthData.Key256Back;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached256BackTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void CachedVarBack_Lookup()
    {
        var keys = WidthData.KeyVarBack;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!CachedVarBackTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }
}

/// <summary>192-bit keys against the 128-bit and 256-bit key types, all with cached masks, in one session.</summary>
[MemoryDiagnoser]
public class Width192Comparison
{
    private static readonly BitTrie<uint, int, string?> Reference = WidthData.BuildRef();
    private static readonly CachedKeyTrie<Bits128, int, string?> Cached128Trie = WidthData.BuildC128();
    private static readonly CachedKeyTrie<Bits192, int, string?> Cached192FrontTrie = WidthData.BuildC192Front();
    private static readonly CachedKeyTrie<Bits192, int, string?> Cached192BackTrie = WidthData.BuildC192Back();
    private static readonly CachedKeyTrie<Bits256, int, string?> Cached256FrontTrie = WidthData.BuildC256Front();
    private static readonly CachedKeyTrie<Bits256, int, string?> Cached256BackTrie = WidthData.BuildC256Back();

    [Benchmark(Baseline = true), IterationCount(30)]
    public void Ref32_IBinaryInteger_Lookup()
    {
        var keys = WidthData.KeyReference;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Reference.TryGetMatch(keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached128_Lookup()
    {
        var keys = WidthData.Key128;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached128Trie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached192Front_Lookup()
    {
        var keys = WidthData.Key192Front;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached192FrontTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached192Back_Lookup()
    {
        var keys = WidthData.Key192Back;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached192BackTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached256Front_Lookup()
    {
        var keys = WidthData.Key256Front;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached256FrontTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }

    [Benchmark, IterationCount(30)]
    public void Cached256Back_Lookup()
    {
        var keys = WidthData.Key256Back;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!Cached256BackTrie.TryGetMatch(in keys[keyIndex], out _, out var value) || value != string.Empty)
                throw new Exception();
    }
}

/// <summary>
///     Result shapes: the same lookup returning its result four ways. Prefixes are IPNetwork values and values are strings,
///     so results contain object references, as real ones would. The large table is the usual 97,000 prefixes. The small
///     table is the first 1,000 prefixes plus 0.0.0.0/0, so every key matches and the walk is short enough for the cost of
///     returning the result to show.
/// </summary>
[MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class ResultShapeComparison
{
    private static readonly ResultShapeTrie<Bits32, IPNetwork, string> Large = Build(Data.Prefixes.Count);
    private static readonly ResultShapeTrie<Bits32, IPNetwork, string> Small = Build(1_000, addDefault: true);

    private static ResultShapeTrie<Bits32, IPNetwork, string> Build(int count, bool addDefault = false)
    {
        var trie = new ResultShapeTrie<Bits32, IPNetwork, string>();
        for (var prefixIndex = 0; prefixIndex < count; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], string.Empty);
        if (addDefault)
            trie.TryAdd(new Bits32(0), 0, IPNetwork.Parse("0.0.0.0/0"), string.Empty);
        return trie;
    }

    public static void Verify()
    {
        var reference = WidthData.BuildRef();
        int mismatches = 0;
        for (var keyIndex = 0; keyIndex < WidthData.KeyReference.Length; keyIndex++)
        {
            reference.TryGetMatch(WidthData.KeyReference[keyIndex], out var index, out _);
            var expected = Data.IPNetworks[index];
            Large.TryGetMatch(in WidthData.Key32[keyIndex], out var outParameterPrefix, out _);
            Large.TryGetMatch(in WidthData.Key32[keyIndex], out PrefixMatch<IPNetwork, string> outStructMatch);
            var returnedMatch = Large.GetMatch(in WidthData.Key32[keyIndex]);
            var returnedEntry = Large.FindEntry(in WidthData.Key32[keyIndex]);
            if (outParameterPrefix != expected || outStructMatch.Prefix != expected || returnedMatch.Prefix != expected || returnedEntry?.Prefix != expected)
                if (mismatches++ < 5)
                    Console.WriteLine($"  MISMATCH key {keyIndex}: expected={expected} a={outParameterPrefix} b={outStructMatch.Prefix} c={returnedMatch.Prefix} d={returnedEntry?.Prefix}");
        }
        Console.WriteLine($"result shapes: lookups compared: {WidthData.KeyReference.Length:N0}, mismatches: {mismatches}");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Large table"), IterationCount(30)]
    public void A_OutParameters_Large() => A(Large);

    [Benchmark, BenchmarkCategory("Large table"), IterationCount(30)]
    public void B_OutStruct_Large() => B(Large);

    [Benchmark, BenchmarkCategory("Large table"), IterationCount(30)]
    public void C_ReturnedStruct_Large() => C(Large);

    [Benchmark, BenchmarkCategory("Large table"), IterationCount(30)]
    public void D_ReturnedEntry_Large() => D(Large);

    [Benchmark(Baseline = true), BenchmarkCategory("Small table"), IterationCount(30)]
    public void A_OutParameters_Small() => A(Small);

    [Benchmark, BenchmarkCategory("Small table"), IterationCount(30)]
    public void B_OutStruct_Small() => B(Small);

    [Benchmark, BenchmarkCategory("Small table"), IterationCount(30)]
    public void C_ReturnedStruct_Small() => C(Small);

    [Benchmark, BenchmarkCategory("Small table"), IterationCount(30)]
    public void D_ReturnedEntry_Small() => D(Small);

    // each consumer reads both the prefix and the value of every result
    private static void A(ResultShapeTrie<Bits32, IPNetwork, string> trie)
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) || value != string.Empty || prefix.PrefixLength < 0)
                throw new Exception();
    }

    private static void B(ResultShapeTrie<Bits32, IPNetwork, string> trie)
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!trie.TryGetMatch(in keys[keyIndex], out PrefixMatch<IPNetwork, string> match) || match.Value != string.Empty || match.Prefix.PrefixLength < 0)
                throw new Exception();
    }

    private static void C(ResultShapeTrie<Bits32, IPNetwork, string> trie)
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var match = trie.GetMatch(in keys[keyIndex]);
            if (!match.Found || match.Value != string.Empty || match.Prefix.PrefixLength < 0)
                throw new Exception();
        }
    }

    private static void D(ResultShapeTrie<Bits32, IPNetwork, string> trie)
    {
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var entry = trie.FindEntry(in keys[keyIndex]);
            if (entry == null || entry.Value != string.Empty || entry.Prefix.PrefixLength < 0)
                throw new Exception();
        }
    }
}

/// <summary>A 64-byte value, to see whether copying the value out of the trie costs anything.</summary>
public readonly struct Payload64
{
    public readonly long Word0, Word1, Word2, Word3, Word4, Word5, Word6, Word7;
    public Payload64(long value) { Word0 = value; Word1 = Word2 = Word3 = Word4 = Word5 = Word6 = Word7 = value; }
}

/// <summary>
///     The four result shapes on the small table, once with string values and once with 64-byte struct values, in one
///     session. Consumers read the prefix length and one field of the value.
/// </summary>
[MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class LargeValueShapeComparison
{
    private static readonly ResultShapeTrie<Bits32, IPNetwork, string> SmallString = Build<string>(prefixIndex => string.Empty);
    // values start at 1 so that the consumers' zero check never fires, including for the default route (index -1)
    private static readonly ResultShapeTrie<Bits32, IPNetwork, Payload64> SmallPayload = Build(prefixIndex => new Payload64(prefixIndex + 2));

    private static ResultShapeTrie<Bits32, IPNetwork, T> Build<T>(Func<int, T> value)
    {
        var trie = new ResultShapeTrie<Bits32, IPNetwork, T>();
        for (var prefixIndex = 0; prefixIndex < 1_000; prefixIndex++)
            trie.TryAdd(in WidthData.Prefix32[prefixIndex], WidthData.PrefixLengths[prefixIndex], Data.IPNetworks[prefixIndex], value(prefixIndex));
        trie.TryAdd(new Bits32(0), 0, IPNetwork.Parse("0.0.0.0/0"), value(-1));
        return trie;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("string value"), IterationCount(30)]
    public void A_OutParameters_String()
    {
        var trie = SmallString;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) || value != string.Empty || prefix.PrefixLength < 0)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("string value"), IterationCount(30)]
    public void B_OutStruct_String()
    {
        var trie = SmallString;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!trie.TryGetMatch(in keys[keyIndex], out PrefixMatch<IPNetwork, string> match) || match.Value != string.Empty || match.Prefix.PrefixLength < 0)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("string value"), IterationCount(30)]
    public void C_ReturnedStruct_String()
    {
        var trie = SmallString;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var match = trie.GetMatch(in keys[keyIndex]);
            if (!match.Found || match.Value != string.Empty || match.Prefix.PrefixLength < 0)
                throw new Exception();
        }
    }

    [Benchmark, BenchmarkCategory("string value"), IterationCount(30)]
    public void D_ReturnedEntry_String()
    {
        var trie = SmallString;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var entry = trie.FindEntry(in keys[keyIndex]);
            if (entry == null || entry.Value != string.Empty || entry.Prefix.PrefixLength < 0)
                throw new Exception();
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("64-byte value"), IterationCount(30)]
    public void A_OutParameters_Payload()
    {
        var trie = SmallPayload;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value) || value.Word0 == 0 || prefix.PrefixLength < 0)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("64-byte value"), IterationCount(30)]
    public void B_OutStruct_Payload()
    {
        var trie = SmallPayload;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            if (!trie.TryGetMatch(in keys[keyIndex], out PrefixMatch<IPNetwork, Payload64> match) || match.Value.Word0 == 0 || match.Prefix.PrefixLength < 0)
                throw new Exception();
    }

    [Benchmark, BenchmarkCategory("64-byte value"), IterationCount(30)]
    public void C_ReturnedStruct_Payload()
    {
        var trie = SmallPayload;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var match = trie.GetMatch(in keys[keyIndex]);
            if (!match.Found || match.Value.Word0 == 0 || match.Prefix.PrefixLength < 0)
                throw new Exception();
        }
    }

    [Benchmark, BenchmarkCategory("64-byte value"), IterationCount(30)]
    public void D_ReturnedEntry_Payload()
    {
        var trie = SmallPayload;
        var keys = WidthData.Key32;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var entry = trie.FindEntry(in keys[keyIndex]);
            if (entry == null || entry.Value.Word0 == 0 || entry.Prefix.PrefixLength < 0)
                throw new Exception();
        }
    }
}
