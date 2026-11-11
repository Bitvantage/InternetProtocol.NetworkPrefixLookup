/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

/// <summary>The layout of a <see cref="FrozenBitTrie{TKey,TPrefix,TValue}" />'s lookup tables.</summary>
public enum FrozenLayout
{
    /// <summary>
    ///     Leaf-pushed tables: a root table over the first bits of the key, and child tables wherever a block still holds
    ///     a more specific prefix. Every entry that is not a child table holds the final answer for its block, so a
    ///     lookup reads only tables. The fastest layout.
    /// </summary>
    LeafPushed,

    /// <summary>
    ///     Compressed tables, after Poptrie: a root table, then nodes that each decide 6 more bits and store their
    ///     children and their runs of equal answers once, found by popcount. Less memory than <see cref="LeafPushed" />:
    ///     about 60% of it on a full IPv4 routing table, and under a tenth for prefixes scattered across the address
    ///     space, where lookups are faster too. Elsewhere lookups are somewhat slower.
    /// </summary>
    Compressed
}

/// <summary>Options for <see cref="BitTrie{TKey,TPrefix,TValue}.Freeze" />.</summary>
public sealed record FrozenBitTrieOptions
{
    /// <summary>
    ///     The layout of the lookup tables, or null, the default, to choose it from the data:
    ///     <see cref="FrozenLayout.LeafPushed" />, unless its child tables would hold more than 256 entries per prefix and
    ///     more than 4,194,304 entries in all, or more than fit in an array; then <see cref="FrozenLayout.Compressed" />,
    ///     which for such scattered prefixes is both much smaller and faster.
    /// </summary>
    public FrozenLayout? Layout { get; init; }

    /// <summary>
    ///     The number of key bits the root table decides, from 1 to 24, and at most the key width. By default it follows
    ///     the number of prefixes, so that a small table gets a small root: for <see cref="FrozenLayout.LeafPushed" />, 8
    ///     bits below 32 prefixes and 16 bits from there; for <see cref="FrozenLayout.Compressed" />, about 16 root
    ///     entries per prefix, from 8 bits up to 18.
    /// </summary>
    public int? RootBits { get; init; }

    /// <summary>
    ///     The number of key bits each child table decides in the <see cref="FrozenLayout.LeafPushed" /> layout, from 1 to
    ///     16. The default is 8.
    /// </summary>
    public int TableBits { get; init; } = 8;

    /// <summary>
    ///     Keeps a dense copy of the values, so that <see cref="FrozenBitTrie{TKey,TPrefix,TValue}.TryGetMatchValue" />
    ///     reads a smaller array. The default is true; turn it off for large value types when only full matches are used.
    /// </summary>
    public bool ValueOnlyLookups { get; init; } = true;
}

/// <summary>
///     A read-only set of prefixes with faster lookups than <see cref="BitTrie{TKey,TPrefix,TValue}" />, built by
///     <see cref="BitTrie{TKey,TPrefix,TValue}.Freeze" />.
/// </summary>
/// <remarks>
///     <para>
///         Lookups read only the tables and an array of entries in address order; there are no trie nodes. Each entry
///         also records its bits, its length and the nearest shorter stored prefix that contains it, which answer the
///         queries about prefixes: the matches follow the nearest shorter prefixes, and the prefixes inside a prefix, being
///         in address order, are one run of entries that a search through sampled keys finds.
///     </para>
///     <para>A frozen trie never changes, so any number of threads may read it.</para>
/// </remarks>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TPrefix">The type of the prefix object stored with each prefix.</typeparam>
/// <typeparam name="TValue">The type of the value stored with each prefix.</typeparam>
public sealed class FrozenBitTrie<TKey, TPrefix, TValue> : IReadOnlyBitTrie<TKey, TPrefix, TValue>
    where TKey : struct, IBitKey<TKey>
{
    internal struct Entry
    {
        public TPrefix Prefix;
        public TValue Value;
    }

    /// <summary>The prefixes copied out of a read-write trie: all that building the tables reads.</summary>
    /// <remarks>
    ///     Freezing copies the prefixes out while it holds the read-write trie's writer lock, then builds the tables from
    ///     this copy after releasing it, so writers wait only for the copy: 69 ms on the real IPv4 table instead of a
    ///     whole freeze of 98 to 112 ms.
    /// </remarks>
    internal sealed class Contents
    {
        public readonly TKey[] Bits;
        public readonly byte[] Lengths;
        public readonly Entry[] Entries;
        public readonly int[] Parents; // the nearest shorter stored prefix containing each entry, or -1

        /// <summary>Copies prefixes in address order, a shorter prefix before a longer one at the same address.</summary>
        public Contents(IEnumerable<(TKey Bits, int Length, TPrefix Prefix, TValue Value)> items, int count)
        {
            Bits = new TKey[count];
            Lengths = new byte[count];
            Entries = new Entry[count];
            Parents = new int[count];

            // the open chain of prefixes that contain the current one, shortest first
            var chain = new List<int>();
            var entryIndex = 0;
            foreach (var (key, length, prefix, value) in items)
            {
                if (entryIndex == count)
                    throw new InvalidOperationException("The trie changed while it was being frozen.");
                Bits[entryIndex] = key;
                Lengths[entryIndex] = (byte)length;
                Entries[entryIndex] = new Entry { Prefix = prefix, Value = value };

                while (chain.Count > 0 && !TKey.MatchesUnderMask(key, Bits[chain[^1]], TKey.MaskFor(Lengths[chain[^1]])))
                    chain.RemoveAt(chain.Count - 1);
                Parents[entryIndex] = chain.Count > 0 ? chain[^1] : -1;
                chain.Add(entryIndex);
                entryIndex++;
            }

            if (entryIndex != count)
                throw new InvalidOperationException("The trie changed while it was being frozen.");
        }
    }

    private struct CompressedNode
    {
        public ulong Children;  // bit s set: slot s leads to a child node
        public ulong Runs;      // bit s set: slot s is an answer that differs from the answer in the previous answer slot
        public int FirstChild;  // index of the child for the lowest child slot; the other children follow it
        public int FirstAnswer; // index in _answers of the first run's answer; the other runs' answers follow it
    }

    // A table entry is an entry index + 1, or 0 for no match, unless TableFlag is set: then the rest is the start of a
    // child table, or the index of a compressed node.
    private const uint TableFlag = 0x8000_0000;
    private const int Stride = 6; // bits per compressed node
    private const int KeyFanout = 16; // keys of the level below per key of _keyLevels

    private readonly Entry[] _entries;
    private readonly TValue[]? _values;
    private readonly TKey[] _keys; // the bits of each entry, for GetSubnets and Overlaps
    private readonly TKey[][] _keyLevels; // every 16th key of _keys, every 16th of those, and so on; see LowerBound
    private readonly byte[] _lengths;
    private readonly int[] _parents; // the nearest shorter stored prefix containing each entry, or -1

    private readonly uint[] _root;
    private readonly int _rootBits;
    private readonly bool _compressed;
    private readonly uint[] _tables = Array.Empty<uint>();
    private readonly int _tableBits;
    private readonly CompressedNode[] _nodes = Array.Empty<CompressedNode>();
    private readonly int[] _answers = Array.Empty<int>(); // entry index, or -1 for no match

    /// <summary>Builds the tables from the prefixes copied out of a read-write trie.</summary>
    internal FrozenBitTrie(Contents contents, FrozenBitTrieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var count = contents.Entries.Length;
        if (options.Layout is { } givenLayout && !Enum.IsDefined(givenLayout))
            throw new ArgumentException($"Unknown layout {givenLayout}.", nameof(options));
        if (options.RootBits is < 1 or > 24)
            throw new ArgumentOutOfRangeException(nameof(options), options.RootBits, "RootBits ranges from 1 to 24.");
        if (options.TableBits is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(options), options.TableBits, "TableBits ranges from 1 to 16.");
        _tableBits = options.TableBits;
        Options = options;

        var bits = _keys = contents.Bits;
        _keyLevels = BuildKeyLevels(bits);
        _entries = contents.Entries;
        _lengths = contents.Lengths;
        _parents = contents.Parents;
        if (options.ValueOnlyLookups)
        {
            _values = new TValue[count];
            for (var entryIndex = 0; entryIndex < count; entryIndex++)
                _values[entryIndex] = _entries[entryIndex].Value;
        }

        var builder = new Builder(bits, _lengths, _tableBits);

        // The leaf-pushed child tables are counted first, so that they go into one array of the exact size; the count also
        // decides the layout when the options leave it to the data.
        var leafRootBits = Math.Min(options.RootBits ?? DefaultRootBits(count, compressed: false), TKey.Width);
        long entries = 0;
        if (options.Layout != FrozenLayout.Compressed)
        {
            var counting = new Runs(0, count, 0, leafRootBits, bits, _lengths);
            while (counting.Next(out _, out var runLow, out var runHigh))
                entries += builder.CountTables(runLow, runHigh, leafRootBits);
            entries <<= _tableBits;
        }

        Layout = options.Layout ?? (LeafPushedTooLarge(entries, count) ? FrozenLayout.Compressed : FrozenLayout.LeafPushed);
        _compressed = Layout == FrozenLayout.Compressed;
        _rootBits = _compressed ? Math.Min(options.RootBits ?? DefaultRootBits(count, compressed: true), TKey.Width) : leafRootBits;
        _root = new uint[1 << _rootBits];
        builder.FillAnswers(0, count, 0, _rootBits, _root);
        if (!_compressed)
        {
            if (entries > int.MaxValue)
                throw new InvalidOperationException("The leaf-pushed tables would exceed 2^31 entries; use the compressed layout or other table sizes.");
            _tables = builder.Tables = new uint[entries];
        }

        var runs = new Runs(0, count, 0, _rootBits, bits, _lengths);
        while (runs.Next(out var slot, out var runLow, out var runHigh))
        {
            var best = (int)_root[slot] - 1;
            if (_compressed)
            {
                var node = builder.Nodes.Count;
                builder.Nodes.Add(default);
                builder.BuildNode(node, runLow, runHigh, best, _rootBits);
                _root[slot] = TableFlag | (uint)node;
            }
            else
                _root[slot] = TableFlag | (uint)builder.BuildTable(runLow, runHigh, best, _rootBits);
        }

        if (_compressed)
        {
            _nodes = builder.Nodes.ToArray();
            _answers = builder.Answers.ToArray();
        }
    }

    // Leaf-pushed tables are the fastest on clustered prefixes such as routing tables, but grow with scattered ones: on
    // 100,000 random IPv6 /128 prefixes their child tables held 3,459 entries per prefix, 1.4 GB, where the compressed
    // layout took 56 MB and answered in a third of the time. Measured on eleven tables, the compressed layout was both
    // smaller and faster wherever the child tables held more than 256 entries per prefix, except on tables small enough
    // to stay in the cache, where leaf-pushed was still faster; hence the second condition, 16 MB of child tables. The
    // real IPv4 and IPv6 tables need 7 and 55 entries per prefix. See Architecture.md, "The frozen trie".
    private static bool LeafPushedTooLarge(long entries, int count) =>
        entries > int.MaxValue || (entries > 1 << 22 && entries > 256L * count);

    // Measured on random and real IPv4 prefixes. Leaf-pushed tables are smallest with a root that is a multiple of their
    // 8-bit child tables, but an 8-bit root made lookups 2.5 times slower at 64 prefixes, so only nearly empty tables,
    // where the 256 KB root is almost all of the memory, get it. Compressed tables are as fast or faster with about 16
    // root entries per prefix as with the full 18-bit root, in a fraction of the memory.
    private static int DefaultRootBits(int count, bool compressed)
    {
        if (!compressed)
            return count < 32 ? 8 : 16;
        var ceilingLog2 = count <= 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(count - 1));
        return Math.Clamp(ceilingLog2 + 4, 8, 18);
    }

    /// <summary>The options the trie was built with.</summary>
    public FrozenBitTrieOptions Options { get; }

    /// <summary>The layout of the lookup tables: the one the options name, or the one chosen from the data.</summary>
    public FrozenLayout Layout { get; }

    /// <summary>The number of stored prefixes.</summary>
    public int Count => _entries.Length;

    /// <summary>The memory held by the tables and entries, in bytes, computed from the array sizes.</summary>
    public long Bytes =>
        ArrayBytes(_root.Length, sizeof(uint)) + ArrayBytes(_tables.Length, sizeof(uint)) + ArrayBytes(_nodes.Length, Unsafe.SizeOf<CompressedNode>())
        + ArrayBytes(_answers.Length, sizeof(int)) + ArrayBytes(_entries.Length, Unsafe.SizeOf<Entry>())
        + ArrayBytes(_values?.Length ?? 0, Unsafe.SizeOf<TValue>()) + ArrayBytes(_keys.Length, Unsafe.SizeOf<TKey>())
        + ArrayBytes(_lengths.Length, sizeof(byte)) + ArrayBytes(_parents.Length, sizeof(int)) + KeyLevelBytes();

    private static long ArrayBytes(int length, int elementSize) => length == 0 ? 0 : 24 + (long)length * elementSize;

    private long KeyLevelBytes()
    {
        var bytes = ArrayBytes(_keyLevels.Length, IntPtr.Size);
        foreach (var level in _keyLevels)
            bytes += ArrayBytes(level.Length, Unsafe.SizeOf<TKey>());
        return bytes;
    }

    // ---- lookups ---------------------------------------------------------------------------------------------------

    // the entry index of the longest match for a full-width key, or -1
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Find(in TKey key)
    {
        var tableEntry = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_root), TKey.GetTopBits(key, _rootBits));
        if ((tableEntry & TableFlag) == 0)
            return (int)tableEntry - 1;
        return _compressed ? FindCompressed(in key, tableEntry) : FindLeafPushed(in key, tableEntry);
    }

    private int FindLeafPushed(in TKey key, uint tableEntry)
    {
        ref var tables = ref MemoryMarshal.GetArrayDataReference(_tables);
        var offset = _rootBits;
        var tableBits = _tableBits;
        do
        {
            tableEntry = Unsafe.Add(ref tables, (int)(tableEntry & ~TableFlag) + TKey.GetBits(key, offset, tableBits));
            offset += tableBits;
        } while ((tableEntry & TableFlag) != 0);

        return (int)tableEntry - 1;
    }

    private int FindCompressed(in TKey key, uint tableEntry)
    {
        ref var nodes = ref MemoryMarshal.GetArrayDataReference(_nodes);
        var nodeIndex = (int)(tableEntry & ~TableFlag);
        var offset = _rootBits;
        while (true)
        {
            ref var node = ref Unsafe.Add(ref nodes, nodeIndex);
            var slot = TKey.GetBits(key, offset, Stride);
            var upToSlot = ulong.MaxValue >> (63 - slot); // bits 0 to slot
            if ((node.Children & (1UL << slot)) == 0)
                return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_answers), node.FirstAnswer + BitOperations.PopCount(node.Runs & upToSlot) - 1);
            nodeIndex = node.FirstChild + BitOperations.PopCount(node.Children & upToSlot) - 1;
            offset += Stride;
        }
    }

    // the longest stored prefix that contains the prefix (bits, length), or -1: every stored prefix that contains it also
    // contains its first address, so it lies on the chain from that address's longest match
    private int FindForPrefix(in TKey bits, int length)
    {
        var entryIndex = Find(TKey.Truncate(bits, length));
        while (entryIndex >= 0 && _lengths[entryIndex] > length)
            entryIndex = _parents[entryIndex];
        return entryIndex;
    }

    private int FindExact(in TKey bits, int length)
    {
        var entryIndex = FindForPrefix(in bits, length);
        return entryIndex >= 0 && _lengths[entryIndex] == length ? entryIndex : -1;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(in TKey key, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value)
    {
        var entryIndex = Find(in key);
        if (entryIndex < 0)
        {
            prefix = default!;
            value = default!;
            return false;
        }

        ref var entry = ref _entries[entryIndex];
        prefix = entry.Prefix;
        value = entry.Value;
        return true;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsMatch(in TKey key) => Find(in key) >= 0; // reads no entry or value

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchValue(in TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var entryIndex = Find(in key);
        if (entryIndex < 0)
        {
            value = default!;
            return false;
        }

        value = _values != null ? _values[entryIndex] : _entries[entryIndex].Value;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetMatch(in TKey bits, int length, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value)
    {
        CheckLength(length);
        var entryIndex = FindForPrefix(in bits, length);
        if (entryIndex < 0)
        {
            prefix = default!;
            value = default!;
            return false;
        }

        ref var entry = ref _entries[entryIndex];
        prefix = entry.Prefix;
        value = entry.Value;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetValue(in TKey bits, int length, [MaybeNullWhen(false)] out TValue value)
    {
        CheckLength(length);
        var entryIndex = FindExact(in bits, length);
        if (entryIndex < 0)
        {
            value = default!;
            return false;
        }

        value = _entries[entryIndex].Value;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetValue(in TKey bits, int length, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value)
    {
        CheckLength(length);
        var entryIndex = FindExact(in bits, length);
        if (entryIndex < 0)
        {
            prefix = default!;
            value = default!;
            return false;
        }

        ref var entry = ref _entries[entryIndex];
        prefix = entry.Prefix;
        value = entry.Value;
        return true;
    }

    /// <inheritdoc />
    public bool ContainsPrefix(in TKey bits, int length)
    {
        CheckLength(length);
        return FindExact(in bits, length) >= 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(in TKey key) => MatchList(Find(in key));

    /// <inheritdoc />
    public int GetMatches(in TKey key, Span<PrefixEntry<TPrefix, TValue>> destination) => WriteMatches(Find(in key), destination);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(in TKey bits, int length)
    {
        CheckLength(length);
        return MatchList(FindForPrefix(in bits, length));
    }

    /// <inheritdoc />
    public int GetMatches(in TKey bits, int length, Span<PrefixEntry<TPrefix, TValue>> destination)
    {
        CheckLength(length);
        return WriteMatches(FindForPrefix(in bits, length), destination);
    }

    // the entry entryIndex and the stored prefixes that contain it, longest first
    private List<PrefixEntry<TPrefix, TValue>> MatchList(int entryIndex)
    {
        var results = new List<PrefixEntry<TPrefix, TValue>>();
        for (; entryIndex >= 0; entryIndex = _parents[entryIndex])
            results.Add(new PrefixEntry<TPrefix, TValue>(_entries[entryIndex].Prefix, _entries[entryIndex].Value));
        return results;
    }

    private int WriteMatches(int entryIndex, Span<PrefixEntry<TPrefix, TValue>> destination)
    {
        var count = 0;
        for (; entryIndex >= 0; entryIndex = _parents[entryIndex])
        {
            if (count == destination.Length)
                throw new ArgumentException("The destination is too small for the matches.", nameof(destination));
            destination[count++] = new PrefixEntry<TPrefix, TValue>(_entries[entryIndex].Prefix, _entries[entryIndex].Value);
        }

        return count;
    }

    /// <inheritdoc />
    public bool Overlaps(in TKey bits, int length)
    {
        CheckLength(length);
        var first = TKey.Truncate(bits, length);

        // A stored prefix that contains the prefix's first address either contains the prefix or lies inside it. Failing
        // that, a prefix inside it starts at a later address, and is the first entry at or after it.
        if (Find(in first) >= 0)
            return true;
        var entryIndex = LowerBound(in first, length);
        return entryIndex < _keys.Length && IsInside(entryIndex, in first, TKey.MaskFor(length), length);
    }

    /// <inheritdoc />
    public IEnumerable<PrefixEntry<TPrefix, TValue>> GetSubnets(in TKey bits, int length)
    {
        CheckLength(length); // here rather than in the iterator, so that a bad length throws at the call
        return Subnets(TKey.Truncate(bits, length), length);
    }

    private IEnumerable<PrefixEntry<TPrefix, TValue>> Subnets(TKey bits, int length)
    {
        var mask = TKey.MaskFor(length);
        for (var entryIndex = LowerBound(in bits, length); entryIndex < _keys.Length && IsInside(entryIndex, in bits, in mask, length); entryIndex++)
            yield return new PrefixEntry<TPrefix, TValue>(_entries[entryIndex].Prefix, _entries[entryIndex].Value);
    }

    // The first entry at or after the prefix (bits, length) in address order, where bits are truncated to length; the
    // entries inside the prefix are the run that starts there. The prefixes that contain it and start at the same
    // address are shorter, so they come before.
    //
    // A binary search over a million keys makes about 20 reads far apart, most of them cache misses on a large table.
    // Instead each level of _keyLevels narrows the search on the level below to the 15 keys after the last key below
    // bits, which lie together: on the full IPv4 table, five levels and then the keys, each a short scan of adjacent
    // keys. Counting the keys below bits instead of stopping at the first that is not was slower.
    private int LowerBound(in TKey bits, int length)
    {
        // The first key at or above bits on a level is at position; on the level below, the first such key comes after
        // the key sampled at position - 1, which is below bits, and at or before the key sampled at position.
        var position = 0;
        var high = _keyLevels.Length > 0 ? _keyLevels[^1].Length : _keys.Length;
        for (var level = _keyLevels.Length - 1; level >= -1; level--)
        {
            var levelKeys = level >= 0 ? _keyLevels[level] : _keys;
            var low = position == 0 ? 0 : (position - 1) * KeyFanout + 1;
            if (level < _keyLevels.Length - 1)
                high = (int)Math.Min((long)position * KeyFanout, levelKeys.Length);
            for (position = low; position < high && TKey.Compare(in levelKeys[position], in bits) < 0; position++) { }
        }

        // the entries at bits itself that are shorter than length contain the prefix, and come first
        while (position < _keys.Length && _lengths[position] < length && TKey.Compare(in _keys[position], in bits) == 0)
            position++;
        return position;
    }

    // true when entry entryIndex lies inside the prefix (bits, length), whose mask is mask
    private bool IsInside(int entryIndex, in TKey bits, in TKey mask, int length) => _lengths[entryIndex] >= length && TKey.MatchesUnderMask(in _keys[entryIndex], in bits, in mask);

    /// <summary>Returns the stored prefixes in address order, a shorter prefix before a longer one at the same address.</summary>
    public IEnumerator<PrefixEntry<TPrefix, TValue>> GetEnumerator()
    {
        foreach (var entry in _entries)
            yield return new PrefixEntry<TPrefix, TValue>(entry.Prefix, entry.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void CheckLength(int length)
    {
        if ((uint)length > (uint)TKey.Width)
            throw new ArgumentOutOfRangeException(nameof(length), length, $"Prefix lengths range from 0 to {TKey.Width}.");
    }

    // ---- building --------------------------------------------------------------------------------------------------

    // Every 16th key of keys, then every 16th key of that level, up to a level of at most 16 keys. The levels add a
    // fifteenth to the keys' memory.
    private static TKey[][] BuildKeyLevels(TKey[] keys)
    {
        var levels = new List<TKey[]>();
        for (var below = keys; below.Length > KeyFanout; below = levels[^1])
        {
            var level = new TKey[(below.Length + KeyFanout - 1) / KeyFanout];
            for (var levelIndex = 0; levelIndex < level.Length; levelIndex++)
                level[levelIndex] = below[levelIndex * KeyFanout];
            levels.Add(level);
        }

        return levels.ToArray();
    }

    // The runs of entries in [low, high) longer than decided + bits, grouped by the slot their next `bits` bits select. The
    // entries come in address order, so the entries below one slot are contiguous: a shorter prefix that starts in a
    // slot's block covers the whole block, and so comes before the longer ones.
    private struct Runs(int low, int high, int decided, int bits, TKey[] keys, byte[] lengths)
    {
        private int _entryIndex = low;

        public bool Next(out int slot, out int runLow, out int runHigh)
        {
            var end = decided + bits;
            while (_entryIndex < high && lengths[_entryIndex] <= end)
                _entryIndex++;
            if (_entryIndex == high)
            {
                slot = runLow = runHigh = 0;
                return false;
            }

            slot = TKey.GetBits(keys[_entryIndex], decided, bits);
            runLow = _entryIndex;
            for (_entryIndex++; _entryIndex < high && lengths[_entryIndex] > end && TKey.GetBits(keys[_entryIndex], decided, bits) == slot; _entryIndex++) { }
            runHigh = _entryIndex;
            return true;
        }
    }

    private sealed class Builder(TKey[] keys, byte[] lengths, int tableBits)
    {
        public uint[] Tables = Array.Empty<uint>();
        private int _nextTable;
        public readonly List<CompressedNode> Nodes = new();
        public readonly List<int> Answers = new();

        // Writes the best match for each of the 2^bits slots of the block of keys that share the first `decided` bits:
        // entry index + 1, or 0, starting from what the slots already hold. Only the entries in [low, high) that end within
        // the slots' bits count; an entry of length L covers 2^(decided + bits - L) slots. When decided + bits passes the
        // key width, lookups read zeros for the missing bits, which this agrees with. A later entry that overlaps an
        // earlier one lies inside it, because containing prefixes come first, so it wins.
        public void FillAnswers(int low, int high, int decided, int bits, Span<uint> slots)
        {
            var end = decided + bits;
            for (var entryIndex = low; entryIndex < high; entryIndex++)
            {
                int length = lengths[entryIndex];
                if (length > end)
                    continue;
                var first = length == decided ? 0 : TKey.GetBits(keys[entryIndex], decided, length - decided) << (end - length);
                slots.Slice(first, 1 << (end - length)).Fill((uint)(entryIndex + 1));
            }
        }

        // a leaf-pushed table for the entries [low, high) inside the block of keys that share the first `decided` bits;
        // returns its start
        public int BuildTable(int low, int high, int best, int decided)
        {
            var start = _nextTable;
            _nextTable += 1 << tableBits;
            var slots = Tables.AsSpan(start, 1 << tableBits);
            slots.Fill((uint)(best + 1));
            FillAnswers(low, high, decided, tableBits, slots);

            var runs = new Runs(low, high, decided, tableBits, keys, lengths);
            while (runs.Next(out var slot, out var runLow, out var runHigh))
            {
                var child = BuildTable(runLow, runHigh, (int)Tables[start + slot] - 1, decided + tableBits);
                Tables[start + slot] = TableFlag | (uint)child;
            }

            return start;
        }

        // the number of leaf-pushed tables BuildTable makes for the same arguments
        public long CountTables(int low, int high, int decided)
        {
            long count = 1;
            var runs = new Runs(low, high, decided, tableBits, keys, lengths);
            while (runs.Next(out _, out var runLow, out var runHigh))
                count += CountTables(runLow, runHigh, decided + tableBits);
            return count;
        }

        // fills Nodes[nodeIndex] for the entries [low, high) inside the block of keys that share the first `decided` bits
        public void BuildNode(int nodeIndex, int low, int high, int best, int decided)
        {
            const int slotCount = 1 << Stride;
            Span<uint> slots = stackalloc uint[slotCount];
            Span<int> runLows = stackalloc int[slotCount];
            Span<int> runHighs = stackalloc int[slotCount];
            slots.Fill((uint)(best + 1));
            FillAnswers(low, high, decided, Stride, slots);

            ulong children = 0;
            var runs = new Runs(low, high, decided, Stride, keys, lengths);
            while (runs.Next(out var slot, out var runLow, out var runHigh))
            {
                children |= 1UL << slot;
                runLows[slot] = runLow;
                runHighs[slot] = runHigh;
            }

            // one answer per run of equal answers, skipping the child slots
            ulong runBits = 0;
            var firstAnswer = Answers.Count;
            var last = uint.MaxValue;
            for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                if ((children & (1UL << slotIndex)) != 0 || slots[slotIndex] == last)
                    continue;
                runBits |= 1UL << slotIndex;
                Answers.Add((int)slots[slotIndex] - 1);
                last = slots[slotIndex];
            }

            // children of one node are contiguous, so reserve their places before building any of them
            var firstChild = Nodes.Count;
            CollectionsMarshal.SetCount(Nodes, firstChild + BitOperations.PopCount(children));
            Nodes[nodeIndex] = new CompressedNode { Children = children, Runs = runBits, FirstChild = firstChild, FirstAnswer = firstAnswer };

            var child = firstChild;
            for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
                if ((children & (1UL << slotIndex)) != 0)
                    BuildNode(child++, runLows[slotIndex], runHighs[slotIndex], (int)slots[slotIndex] - 1, decided + Stride);
        }
    }
}
