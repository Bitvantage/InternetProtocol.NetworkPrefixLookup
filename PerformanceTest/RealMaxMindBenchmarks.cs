/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;
using BenchmarkDotNet.Attributes;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;
using MaxMind.Db;

namespace PerformanceTest;

/// <summary>
///     The MaxMind loader and lookups on a database the size of a full table: the RouteViews IPv4 and IPv6 tables written
///     by <see cref="MaxMindWriter" /> as an ASN database, in the temporary folder. Unlike <see cref="MaxMindBenchmarks" />,
///     whose test database fits in the cache, this one does not. Set ROUTEVIEWS_DIR; see <see cref="RouteViews" />.
/// </summary>
[MemoryDiagnoser]
public class RealMaxMindBenchmarks
{
    private static readonly Lazy<Reader> Reader = new(() => new Reader(CreateDatabase(), FileAccessMode.Memory));
    private static readonly Lazy<MaxMindNetworkLookup<Dictionary<string, object>>> Lookup = new(() => MaxMindDatabase.Load<Dictionary<string, object>>(Reader.Value));
    private static readonly Lazy<FrozenMaxMindNetworkLookup<Dictionary<string, object>>> Frozen = new(() => Lookup.Value.Freeze());

    // 200,000 addresses inside announced prefixes, half IPv4 and half IPv6
    private static readonly Lazy<IPAddress[]> Addresses = new(() => RouteViews.IPv4.Addresses.Take(100_000).Concat(RouteViews.IPv6.Addresses.Take(100_000)).ToArray());

    // Written once per pair of tables and kept. IPv6 prefixes inside ::/96, such as ::/8, are left out: in a MaxMind
    // tree they would cover the IPv4 networks.
    private static string CreateDatabase()
    {
        var (ipv4File, ipv4Rows) = ComparisonData.ReadRouteViews("rv2", 32);
        var (ipv6File, ipv6Rows) = ComparisonData.ReadRouteViews("rv6", 128);
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(ipv4File)}-{Path.GetFileNameWithoutExtension(ipv6File)}-asn.mmdb");
        if (!File.Exists(path))
            MaxMindWriter.Write(path, ipv4Rows.Select(row => (row.Bits, row.Length + 96, (uint)row.Asn)).Concat(ipv6Rows.Where(row => row.Bits >> 32 != 0).Select(row => (row.Bits, row.Length, (uint)row.Asn))), "RouteViews-ASN");
        return path;
    }

    [GlobalSetup]
    public void Setup()
    {
        // the written database must answer as MaxMind's reader does, or the numbers measure nothing
        var mismatches = Addresses.Value.Where((_, addressIndex) => addressIndex % 10 == 0).Count(address =>
            !Equals(Reader.Value.Find<Dictionary<string, object>>(address)?["autonomous_system_number"],
                Frozen.Value.TryGetMatchValue(address, out var record) ? record["autonomous_system_number"] : null));
        if (mismatches != 0)
            throw new InvalidOperationException($"The lookup and MaxMind's reader disagree at {mismatches:N0} of 20,000 addresses.");
        Console.WriteLine($"// {Lookup.Value.IPv4Count:N0} IPv4 and {Lookup.Value.IPv6Count:N0} IPv6 networks; the lookup agrees with the reader");
    }

    [Benchmark, IterationCount(10)]
    public object Load() => MaxMindDatabase.Load<Dictionary<string, object>>(Reader.Value);

    [Benchmark(Baseline = true)]
    public int Reader_Find()
    {
        var reader = Reader.Value;
        var found = 0;
        foreach (var address in Addresses.Value)
            if (reader.Find<Dictionary<string, object>>(address) != null)
                found++;
        return found;
    }

    [Benchmark]
    public int Lookup_ReadWrite()
    {
        var lookup = Lookup.Value;
        var found = 0;
        foreach (var address in Addresses.Value)
            if (lookup.TryGetMatchValue(address, out _))
                found++;
        return found;
    }

    [Benchmark]
    public int Lookup_Frozen()
    {
        var lookup = Frozen.Value;
        var found = 0;
        foreach (var address in Addresses.Value)
            if (lookup.TryGetMatchValue(address, out _))
                found++;
        return found;
    }
}
