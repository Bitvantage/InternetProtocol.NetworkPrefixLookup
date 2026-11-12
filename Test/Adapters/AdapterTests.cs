/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;
using MaxMind.Db;

namespace Test.Adapters;

/// <summary>Tests for the adapter packages: each reads its library's type correctly, in both families and both forms.</summary>
public class AdapterTests
{
    [Test]
    public void IPNetwork2_lookups()
    {
        var lookup = new IPNetwork2Lookup<int>();
        lookup.Add(IPNetwork2.Parse("10.0.0.0/8"), 8);
        lookup.Add(IPNetwork2.Parse("10.128.0.0/9"), 9);
        lookup.Add(IPNetwork2.Parse("fd00::/8"), 108);
        lookup.Add(IPNetwork2.Parse("fd12:3456::/32"), 132);
        Assert.That(lookup.TryAdd(IPNetwork2.Parse("10.0.0.0/8"), 0), Is.False);

        foreach (IReadOnlyIPPrefixLookup<IPNetwork2, int> form in new IReadOnlyIPPrefixLookup<IPNetwork2, int>[] { lookup, lookup.Freeze() })
        {
            Assert.That(form.TryGetMatchValue(IPAddress.Parse("10.200.0.1"), out var value) && value == 9);
            Assert.That(form.TryGetMatchValue(IPAddress.Parse("10.1.0.1"), out value) && value == 8);
            Assert.That(form.TryGetMatchValue(IPAddress.Parse("fd12:3456::1"), out value) && value == 132);
            Assert.That(form.TryGetMatchValue(IPAddress.Parse("fd99::1"), out value) && value == 108);
            Assert.That(form.TryGetMatchValue(IPAddress.Parse("fe80::1"), out _), Is.False);
            Assert.That(form.TryGetMatch(IPAddress.Parse("10.200.0.1"), out var network, out _) && network.ToString() == "10.128.0.0/9");
            Assert.That(new IPNetwork2Lookup<int>(form).Select(entry => entry.Value), Is.EqualTo(form.Select(entry => entry.Value)), "a copy");
        }

        Assert.That(lookup.Remove(IPNetwork2.Parse("10.128.0.0/9")));
        Assert.That(lookup.TryGetMatchValue(IPAddress.Parse("10.200.0.1"), out var after) && after == 8);
    }

    [Test(Description = "The adapter lookups have parameterless constructors too, and MaxMind's keeps its default of mapping IPv4-mapped addresses.")]
    public void Generic_code_can_create_an_adapter_lookup()
    {
        Assert.That(Create<IPNetwork2Lookup<int>>().Options.MapIPv4MappedAddresses, Is.False);
        Assert.That(Create<MaxMindNetworkLookup<int>>().Options.MapIPv4MappedAddresses, Is.True);
        Assert.That(new MaxMindNetworkLookup<int>(options: null).Options.MapIPv4MappedAddresses, Is.True, "null options are MaxMind's defaults");
        Assert.That(new MaxMindNetworkLookup<int>(null).Options.MapIPv4MappedAddresses, Is.True, "an unnamed null binds to the options, not to a sequence");
        Assert.That(new MaxMindNetworkLookup<int>(new IPLookupOptions()).Options.MapIPv4MappedAddresses, Is.False, "given options are kept");
    }

    private static T Create<T>() where T : new() => new();

    private static string DataPath(string file) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", "MaxMind", file);

    private static string Json(object? record) => JsonSerializer.Serialize(record);

    // the first and last address of every network in the database, with their neighbours, plus random addresses
    private static List<IPAddress> Probes(Reader reader, int seed)
    {
        var probes = new List<IPAddress>();
        foreach (var node in reader.FindAll<Dictionary<string, object>>())
        {
            if (node.PrefixLength < 0)
                continue;
            var isIPv4 = node.Start.AddressFamily == AddressFamily.InterNetwork;
            if (isIPv4)
            {
                var first = IPAddressBits.ToIPv4Bits(node.Start);
                var last = first | (uint)(0xFFFFFFFFUL >> node.PrefixLength);
                // each address as IPv4, IPv4-compatible and IPv4-mapped
                foreach (var address in new[] { first, last, first - 1, last + 1 })
                {
                    probes.Add(IPAddressBits.FromIPv4Bits(address));
                    probes.Add(IPAddressBits.FromIPv6Bits(address));
                    probes.Add(IPAddressBits.FromIPv6Bits(address | ((UInt128)0xFFFF << 32)));
                }
            }
            else
            {
                var first = IPAddressBits.ToIPv6Bits(node.Start);
                var last = first | (node.PrefixLength == 0 ? UInt128.MaxValue : UInt128.MaxValue >> node.PrefixLength);
                probes.AddRange(new[] { first, last, first - 1, last + 1 }.Select(IPAddressBits.FromIPv6Bits));
            }
        }

        var random = new Random(seed);
        for (var randomProbeIndex = 0; randomProbeIndex < 2000; randomProbeIndex++)
        {
            probes.Add(IPAddressBits.FromIPv4Bits((uint)random.NextInt64(0, 1L << 32)));
            var bytes = new byte[16];
            random.NextBytes(bytes);
            probes.Add(new IPAddress(bytes));
        }

        return probes;
    }

    // the aliases of the IPv4 networks in MaxMind's IPv6 databases: IPv4-mapped, Teredo and 6to4
    private static bool InAlias(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;
        var bits = IPAddressBits.ToIPv6Bits(address);
        return bits >> 32 == 0xFFFF || bits >> 96 == 0x20010000 || bits >> 112 == 0x2002;
    }

    // ::/96, where MaxMind's IPv6 databases keep the IPv4 networks
    private static bool IsIPv4Compatible(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && IPAddressBits.ToIPv6Bits(address) >> 32 == 0;

    [TestCase("MaxMind-DB-test-mixed-24.mmdb")]
    [TestCase("GeoIP2-Country-Test.mmdb")]
    public void A_loaded_MaxMind_database_answers_like_the_reader(string file)
    {
        using var reader = new Reader(DataPath(file));
        var probes = Probes(reader, 7);

        // with the aliases loaded and no mapping, every address must match the reader exactly
        var full = MaxMindDatabase.Load<Dictionary<string, object>>(reader, new MaxMindLoadOptions { IncludeAliasedNetworks = true, LookupOptions = new IPLookupOptions { MapIPv4MappedAddresses = false } });
        // by default the aliases are skipped, and IPv4-mapped addresses are looked up as IPv4
        var lean = MaxMindDatabase.Load<Dictionary<string, object>>(DataPath(file));
        Assert.That(lean.Count, Is.LessThanOrEqualTo(full.Count));
        Assert.That(lean.Options.MapIPv4MappedAddresses, "MaxMind lookups map by default");
        Assert.That(new MaxMindNetworkLookup<string>().Options.MapIPv4MappedAddresses);
        var copy = new MaxMindNetworkLookup<Dictionary<string, object>>(lean.Freeze());
        Assert.That(copy.Options.MapIPv4MappedAddresses && copy.Count == lean.Count, "a copy maps by default too");
        Assert.That(new IPNetworkLookup<string>().Options.MapIPv4MappedAddresses, Is.False, "other lookups do not");

        var failures = new List<string>();
        var found = 0;
        foreach (var address in probes)
        {
            var expected = Json(reader.Find<Dictionary<string, object>>(address));
            if (expected != "null")
                found++;
            foreach (var (name, form) in new (string, IReadOnlyIPPrefixLookup<Network, Dictionary<string, object>>)[] { ("full", full), ("full frozen", full.Freeze()), ("lean", lean), ("lean frozen", lean.Freeze()) })
            {
                if (name.StartsWith("lean") && (IsIPv4Compatible(address) || (InAlias(address) && IPAddressBits.ToIPv6Bits(address) >> 32 != 0xFFFF)))
                    continue; // by default, only IPv4-mapped addresses find the IPv4 networks
                var actual = Json(form.TryGetMatchValue(address, out var record) ? record : null);
                if (actual != expected)
                    failures.Add($"{name} at {address}: {actual}, expected {expected}");
            }
        }

        Assert.That(failures, Is.Empty, $"{failures.Count} of {probes.Count * 4}; the first: {failures.FirstOrDefault()}");
        Assert.That(found, Is.GreaterThan(50), "enough probes land in a network");
    }

    // A minimal MaxMind DB: a search tree of 24-bit records, one data record { "id": 1 }, and metadata. records(nodeIndex) gives
    // node nodeIndex's left and right record: a node number, nodeCount for no data, or -1 for the data record.
    private static byte[] TinyDatabase(int nodeCount, Func<int, (int Left, int Right)> records, int ipVersion)
    {
        var tree = new byte[nodeCount * 6];
        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            var (left, right) = records(nodeIndex);
            Write24(tree, nodeIndex * 6, left == -1 ? nodeCount + 16 : left);
            Write24(tree, nodeIndex * 6 + 3, right == -1 ? nodeCount + 16 : right);
        }

        var data = new byte[] { 0xE1, 0x42, (byte)'i', (byte)'d', 0xC1, 0x01 };
        var metadata = new List<byte> { 0xAB, 0xCD, 0xEF };
        metadata.AddRange("MaxMind.com"u8.ToArray());
        metadata.Add(0xE9);
        void Key(string key)
        {
            metadata.Add((byte)(0x40 | key.Length));
            metadata.AddRange(System.Text.Encoding.ASCII.GetBytes(key));
        }

        Key("binary_format_major_version"); metadata.AddRange(new byte[] { 0xA1, 2 });
        Key("binary_format_minor_version"); metadata.Add(0xA0);
        Key("build_epoch"); metadata.AddRange(new byte[] { 0x00, 0x02 });
        Key("database_type"); metadata.Add(0x44); metadata.AddRange("Test"u8.ToArray());
        Key("description"); metadata.Add(0xE0);
        Key("ip_version"); metadata.AddRange(new byte[] { 0xA1, (byte)ipVersion });
        Key("languages"); metadata.AddRange(new byte[] { 0x00, 0x04 });
        Key("node_count"); metadata.AddRange(new byte[] { 0xC2, (byte)(nodeCount >> 8), (byte)nodeCount });
        Key("record_size"); metadata.AddRange(new byte[] { 0xA1, 24 });
        return tree.Concat(new byte[16]).Concat(data).Concat(metadata).ToArray();

        static void Write24(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 16);
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)value;
        }
    }

    [Test]
    public void A_record_above_the_IPv4_networks_answers_IPv4_addresses_as_the_reader_does()
    {
        // an IPv6 tree whose only record is ::/64, which holds the whole IPv4 part at ::/96
        var bytes = TinyDatabase(64, nodeIndex => (nodeIndex == 63 ? -1 : nodeIndex + 1, 64), 6);
        using var reader = new Reader(new MemoryStream(bytes));
        var lookup = MaxMindDatabase.Load<Dictionary<string, object>>(reader);
        var full = MaxMindDatabase.Load<Dictionary<string, object>>(reader, new MaxMindLoadOptions { IncludeAliasedNetworks = true });
        foreach (var text in new[] { "::", "::1", "0:0:0:1::", "1.2.3.4", "::ffff:1.2.3.4", "::1.2.3.4" })
        {
            var address = IPAddress.Parse(text);
            var expected = reader.Find<Dictionary<string, object>>(address) != null;
            Assert.That(lookup.TryGetMatchValue(address, out _), Is.EqualTo(expected), text);
            Assert.That(full.TryGetMatchValue(address, out _), Is.EqualTo(expected), $"{text}, aliases loaded");
        }
    }

    [Test]
    public void The_loader_skips_the_IPv4_aliases_by_default()
    {
        using var reader = new Reader(DataPath("MaxMind-DB-test-mixed-24.mmdb"));
        var full = MaxMindDatabase.Load<Dictionary<string, object>>(reader, new MaxMindLoadOptions { IncludeAliasedNetworks = true });
        var asGiven = MaxMindDatabase.Load<Dictionary<string, object>>(reader, new MaxMindLoadOptions { IncludeAliasedNetworks = true, LookupOptions = new IPLookupOptions() });
        var lean = MaxMindDatabase.Load<Dictionary<string, object>>(reader);
        var ipv4Count = full.Count(entry => entry.Prefix.NetworkAddress.AddressFamily == AddressFamily.InterNetwork);
        Assert.That(ipv4Count, Is.GreaterThan(0));
        Assert.That(asGiven.Count - lean.Count, Is.EqualTo(4 * ipv4Count), "each IPv4 network appears under ::/96 and three aliases");
        Assert.That(full.Count - lean.Count, Is.EqualTo(3 * ipv4Count), "a lookup that maps IPv4-mapped addresses refuses the ::ffff:0:0/96 alias");
        Assert.That(lean.Any(entry => InAlias(entry.Prefix.NetworkAddress)), Is.False);
    }
}
