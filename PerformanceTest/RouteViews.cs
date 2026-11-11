/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Diagnostics;
using System.Net;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

namespace PerformanceTest;

/// <summary>
///     The CAIDA RouteViews prefix-to-AS tables for IPv4 and IPv6, as the prototype uses them. Download them with
///     <c>Prototypes/GenericTrie/download-routeviews.sh</c> and set ROUTEVIEWS_DIR to the folder. Values are origin AS
///     numbers. The one million lookup keys are random addresses inside randomly chosen announced prefixes, made with the
///     prototype's seed, so results compare with Documentation/Optimization-Log.md.
/// </summary>
public static class RouteViews
{
    public sealed record Table(IPNetwork[] Networks, int[] Asns, UInt128[] Keys, IPAddress[] Addresses)
    {
        // IPv4 keys as 4-byte integers, so that a benchmark loop streams 4 MB of keys instead of 16 MB
        public uint[] IPv4Keys { get; } = Keys.Select(key => (uint)key).ToArray();
    }

    private static readonly Lazy<Table> IPv4Table = new(() => Load("rv2", 32));
    private static readonly Lazy<Table> IPv6Table = new(() => Load("rv6", 128));

    public static bool Available => Environment.GetEnvironmentVariable("ROUTEVIEWS_DIR") is { Length: > 0 } directoryPath && Directory.Exists(directoryPath);

    public static Table IPv4 => IPv4Table.Value;

    public static Table IPv6 => IPv6Table.Value;

    public static IPNetworkLookup<int> Build(Table table)
    {
        var lookup = new IPNetworkLookup<int>();
        using var batch = lookup.BeginBatch();
        for (var networkIndex = 0; networkIndex < table.Networks.Length; networkIndex++)
            lookup.TryAdd(table.Networks[networkIndex], table.Asns[networkIndex]);
        batch.Commit();
        return lookup;
    }

    private static Table Load(string kind, int width)
    {
        var stopwatch = Stopwatch.StartNew();
        var (file, rows) = ComparisonData.ReadRouteViews(kind, width);
        var bits = rows.Select(row => row.Bits).ToList();
        var lengths = rows.Select(row => row.Length).ToList();
        var networks = rows.Select(row => new IPNetwork(ToAddress(row.Bits, width), row.Length)).ToList();
        var asns = rows.Select(row => row.Asn).ToList();

        // the prototype's keys: a random announced prefix, then random bits for the rest of the address
        var random = new Random(42);
        var keys = new UInt128[1_000_000];
        var addresses = new IPAddress[keys.Length];
        var noise = new byte[width / 8];
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var prefixIndex = random.Next(bits.Count);
            random.NextBytes(noise);
            var host = width == 32 ? (UInt128)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(noise) : System.Buffers.Binary.BinaryPrimitives.ReadUInt128BigEndian(noise);
            var mask = Mask(lengths[prefixIndex], width);
            keys[keyIndex] = (bits[prefixIndex] & mask) | (host & ~mask & Mask(width, width));
            addresses[keyIndex] = ToAddress(keys[keyIndex], width);
        }

        Console.Error.WriteLine($"loaded {bits.Count:N0} {kind} prefixes from {Path.GetFileName(file)} in {stopwatch.Elapsed.TotalSeconds:F1} s");
        return new Table(networks.ToArray(), asns.ToArray(), keys, addresses);
    }

    // the first length bits of a width-bit number, right-aligned
    private static UInt128 Mask(int length, int width)
    {
        var all = width == 128 ? UInt128.MaxValue : (UInt128.One << width) - 1;
        return length == 0 ? UInt128.Zero : all & ~(all >> length);
    }

    private static IPAddress ToAddress(UInt128 value, int width) => width == 32 ? IPAddressBits.FromIPv4Bits((uint)value) : IPAddressBits.FromIPv6Bits(value);
}
