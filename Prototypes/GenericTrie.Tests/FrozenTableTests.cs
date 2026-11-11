/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using GenericTrie;

namespace GenericTrie.Tests;

/// <summary>
///     Tests for the frozen-table options of PooledTrie: leaf-pushed tables and compressed tables in the style of
///     Poptrie. Each shape is checked against an independent reference, at edge cases, after a later insert, and on the
///     real routing tables.
/// </summary>
[Parallelizable(ParallelScope.None)]
public class FrozenTableTests
{
    // subBits 0 selects compressed tables; the narrow shapes force a last table or node that is narrower than the rest
    public static readonly object[] Shapes =
    {
        new object[] { "leaf-pushed 16/8", 16, 8 },
        new object[] { "leaf-pushed 20/4", 20, 4 },
        new object[] { "leaf-pushed 4/6", 4, 6 },
        new object[] { "compressed 16", 16, 0 },
        new object[] { "compressed 18", 18, 0 },
        new object[] { "compressed 20", 20, 0 },
        new object[] { "compressed 8", 8, 0 }
    };

    private static void Freeze<TKey, TPrefix, TValue>(PooledTrie<TKey, TPrefix, TValue> trie, int rootBits, int subBits, int width)
        where TKey : struct, IBitKey<TKey>
    {
        if (subBits == 0)
            trie.BuildCompressed(rootBits, width);
        else
            trie.BuildLeafPushed(rootBits, subBits, width);
    }

    private static PooledTrie<TKey, int, int> Build<TKey>(TKey[] prefixes, int[] lengths) where TKey : struct, IBitKey<TKey>
    {
        var trie = new PooledTrie<TKey, int, int>();
        for (var prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            trie.TryAdd(in prefixes[prefixIndex], lengths[prefixIndex], prefixIndex, prefixIndex * 7);
        trie.Relayout(NodeOrder.DepthFirst);
        return trie;
    }

    // the reference: the IBinaryInteger trie from the first prototype
    private static int[] ReferenceMatches(uint[] prefixes, int[] lengths, uint[] keys)
    {
        var reference = new BitTrie<uint, int, int>();
        for (var prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            reference.TryAdd(prefixes[prefixIndex], lengths[prefixIndex], prefixIndex, 0);
        return keys.Select(key => reference.TryGetMatch(key, out var prefix, out _) ? prefix : -1).ToArray();
    }

    private static void AssertMatches<TKey>(PooledTrie<TKey, int, int> trie, TKey[] keys, int[] expected, string because) where TKey : struct, IBitKey<TKey>
    {
        var mismatches = 0;
        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            var found = trie.TryGetMatch(in keys[keyIndex], out var prefix, out var value);
            var actual = found ? prefix : -1;
            if (actual != expected[keyIndex] || (found && value != prefix * 7))
                mismatches++;
        }

        Assert.That(mismatches, Is.Zero, because);
    }

    [TestCaseSource(nameof(Shapes))]
    public void Random_IPv4_prefixes_match_the_reference(string name, int rootBits, int subBits)
    {
        var trie = Build(WidthData.Prefix32, WidthData.PrefixLengths);
        Freeze(trie, rootBits, subBits, 32);
        AssertMatches(trie, WidthData.Key32, ReferenceMatches(WidthData.PrefixReference, WidthData.PrefixLengths, WidthData.KeyReference), name);
    }

    [TestCaseSource(nameof(Shapes))]
    public void The_same_prefixes_in_a_128_bit_key_match_the_reference(string name, int rootBits, int subBits)
    {
        // the IPv4 bits sit at the top of the 128-bit key, so the matches are the same as for 32 bits
        var trie = Build(WidthData.Prefix128, WidthData.PrefixLengths);
        Freeze(trie, rootBits, subBits, 128);
        AssertMatches(trie, WidthData.Key128, ReferenceMatches(WidthData.PrefixReference, WidthData.PrefixLengths, WidthData.KeyReference), name);
    }

    [TestCaseSource(nameof(Shapes))]
    public void Edge_cases(string name, int rootBits, int subBits)
    {
        // the default route, host routes, a /31 and prefixes that end exactly on and just off table boundaries
        uint[] prefixes = { 0x00000000, 0x0A000000, 0x0A010000, 0x0A010200, 0x0A010203, 0x0A010204, 0x0A010280, 0x0A0102FF, 0x0A010300, 0x0A010000, 0xFFFFFFFF, 0xC0000000 };
        int[] lengths = { 0, 8, 16, 24, 32, 31, 25, 32, 26, 18, 32, 2 };
        var keys = Enumerable.Range(0, 1024).Select(offset => 0x0A010000u + (uint)offset) // 10.1.0.0 to 10.1.3.255
            .Concat(new uint[] { 0, 0x0A020000, 0x0B000000, 0x7FFFFFFF, 0xC0000001, 0xFFFFFFFE, 0xFFFFFFFF })
            .ToArray();

        foreach (var withDefault in new[] { true, false })
        {
            var used = Enumerable.Range(0, prefixes.Length).Where(prefixIndex => withDefault || lengths[prefixIndex] != 0).ToArray();
            var usedPrefixes = used.Select(prefixIndex => prefixes[prefixIndex]).ToArray();
            var usedLengths = used.Select(prefixIndex => lengths[prefixIndex]).ToArray();
            var trie = Build(usedPrefixes.Select(prefixBits => new Bits32(prefixBits)).ToArray(), usedLengths);
            Freeze(trie, rootBits, subBits, 32);
            AssertMatches(trie, keys.Select(key => new Bits32(key)).ToArray(), ReferenceMatches(usedPrefixes, usedLengths, keys), $"{name}, {(withDefault ? "with" : "without")} the default route");
        }
    }

    [TestCaseSource(nameof(Shapes))]
    public void An_empty_trie_matches_nothing(string name, int rootBits, int subBits)
    {
        var trie = new PooledTrie<Bits32, int, int>();
        Freeze(trie, rootBits, subBits, 32);
        foreach (var key in new uint[] { 0, 0x0A000001, 0xFFFFFFFF })
            Assert.That(trie.TryGetMatch(new Bits32(key), out _, out _), Is.False, name);
    }

    [TestCaseSource(nameof(Shapes))]
    public void Adding_a_prefix_after_freezing_drops_the_tables(string name, int rootBits, int subBits)
    {
        var trie = Build(new[] { new Bits32(0x0A000000) }, new[] { 8 });
        Freeze(trie, rootBits, subBits, 32);
        trie.TryAdd(new Bits32(0x0A010200), 24, 1, 7);
        Assert.That(trie.TryGetMatch(new Bits32(0x0A010203), out var prefix, out _) && prefix == 1, name);
    }

    [Test]
    public void Real_IPv4_table_matches_the_node_walk()
    {
        RequireRealData(); // before touching RealData, which throws without the data
        RealTable(RealData.V4, 32);
    }

    [Test]
    public void Real_IPv6_table_matches_the_node_walk()
    {
        RequireRealData(); // before touching RealData, which throws without the data
        RealTable(RealData.V6, 128);
    }

    private static void RequireRealData()
    {
        if (Environment.GetEnvironmentVariable("ROUTEVIEWS_DIR") == null)
            Assert.Ignore("Set ROUTEVIEWS_DIR to run the real-data tests; see Prototypes/GenericTrie/download-routeviews.sh.");
    }

    private static void RealTable<TKey>(RealData.Table<TKey> table, int width) where TKey : struct, IBitKey<TKey>
    {

        // the node walk was checked against the current library in SnapshotTrieTests and RealDataChecks
        var trie = Build(table.Bits, table.Lengths);
        var expected = table.Keys.Select(key => trie.GetMatchIndex(in key)).ToArray();
        foreach (var (rootBits, subBits) in new[] { (16, 8), (18, 0) })
        {
            var frozen = Build(table.Bits, table.Lengths);
            Freeze(frozen, rootBits, subBits, width);
            var mismatches = 0;
            for (var keyIndex = 0; keyIndex < table.Keys.Length; keyIndex++)
                if (frozen.GetMatchIndex(in table.Keys[keyIndex]) != expected[keyIndex])
                    mismatches++;
            Assert.That(mismatches, Is.Zero, subBits == 0 ? $"compressed {rootBits}" : $"leaf-pushed {rootBits}/{subBits}");
        }
    }
}
