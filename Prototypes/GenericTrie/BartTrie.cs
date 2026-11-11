/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace GenericTrie;

/// <summary>
///     A read-write core in the style of Go's BART, generic over <see cref="IBitKey{TSelf}" />, to decide the
///     redesign's storage. See Documentation/Bart-Comparison.md.
/// </summary>
/// <remarks>
///     <para>
///         The trie has 8-bit strides. A node keeps four 256-bit sets, each with a popcount-compressed array: prefixes
///         that end inside its stride, as Knuth's ART base indexes 1 to 255 for relative lengths /0 to /7; fringes,
///         prefixes that end exactly at the end of the stride, by octet; child nodes, by octet; and leaves, lone longer
///         prefixes stored whole, by octet.
///     </para>
///     <para>
///         The shape is canonical: a child node exists at an octet exactly when two or more prefixes lie below it,
///         and a leaf when one does. Fringes stay in their node whatever happens below them.
///     </para>
///     <para>
///         Readers take no locks. A write copies the one node it changes, finishes the copy, and links it in with one
///         reference write into the parent's child array, or into the root field. Readers see the old node or the new
///         one. Replaced nodes become garbage that the .NET garbage collector frees once no reader holds them, so
///         there is no compaction and no array that grows in place while readers can see it. Nodes created within one
///         write, or during a bulk load, are private and changed in place. Only one writer at a time is supported.
///     </para>
/// </remarks>
public sealed class BartTrie<TKey, TPrefix, TValue> where TKey : struct, IBitKey<TKey>
{
    private struct Entry
    {
        public TPrefix Prefix;
        public TValue Value;
    }

    private struct Leaf
    {
        public TKey Bits;
        public TKey Mask;
        public int Length;
        public Entry Entry;
    }

    // which of a node's arrays it still shares with the node it was copied from; those must be copied before a change
    private const byte PrefixArray = 1, FringeArray = 2, KidArray = 4, LeafArray = 8, AllArrays = 15;

    private sealed class Node
    {
        // read by lookups
        public BitSet256 Kids;
        public BitSet256 Leaves;
        public BitSet256 Fringes;
        public BitSet256 Prefixes;
        public Node[] KidNodes = Array.Empty<Node>();
        public Leaf[] LeafEntries = Array.Empty<Leaf>();
        public Entry[] FringeEntries = Array.Empty<Entry>();
        public Entry[] PrefixEntries = Array.Empty<Entry>();
        public bool HasLocal; // prefixes or fringes in this node; lookups skip nodes without them when backtracking

        // writer only
        public int Count;   // prefixes in this subtree, including fringes and leaves
        public int Stamp;   // the write, or bulk load, that created this node; it may change it in place
        public byte Shared;

        public Node Copy(int stamp)
        {
            var copy = (Node)MemberwiseClone();
            copy.Stamp = stamp;
            copy.Shared = AllArrays;
            return copy;
        }
    }

    // one slot per level of a 128-bit key and the root; wider keys need a larger stack (experiment)
    [InlineArray(17)]
    private struct NodeStack
    {
        private Node _element;
    }

    [InlineArray(17)]
    private struct DepthStack
    {
        private byte _element;
    }

    // a stored entry, for the shortcut table: an entry array never changes once readers can see it
    private sealed class Hit(Entry[] array, int index)
    {
        public readonly Entry[] Array = array;
        public readonly int Index = index;
    }

    // Experiment switches, read once per process so that the JIT folds them. Both are off by default: in one
    // BenchmarkDotNet session (BartVariantBenchmarks) neither made lookups faster on the real tables.
    private static readonly bool Prune = Environment.GetEnvironmentVariable("BART_PRUNE") == "1";
    private static readonly bool SlotBest = Environment.GetEnvironmentVariable("BART_SLOTBEST") == "1";

    // Shortcuts for the first 16 bits: for each /16 block, the node at depth 1 and the node at depth 2 on its path,
    // or null. Lookups start there instead of at the root. A write that replaces a node at depth 1 or 2 updates the
    // slots that point at it. A reader may briefly combine nodes of different versions; since a write changes one
    // prefix, the result is still the answer from before or after that write.
    private struct Slot
    {
        public Node? Depth1;
        public Node? Depth2;
        public Hit? Best; // the longest match among the root's and the depth-1 node's own prefixes, for this /16
    }

    private readonly Slot[]? _table;

    private Node _root;      // what readers see
    private Node _writerRoot; // what the writer works on; ahead of _root only during a bulk load
    private readonly int _width;
    private int _stamp = 1;  // nodes stamped 0 at construction count as published
    private bool _inBulkLoad;
    private long _work;      // array elements copied and nodes copied, for measuring work per write

    /// <summary>Creates an empty trie for keys of <paramref name="width" /> bits, a multiple of 8.</summary>
    public BartTrie(int width)
    {
        if (width % 8 != 0 || width > 256)
            throw new ArgumentOutOfRangeException(nameof(width));
        _width = width;
        _root = _writerRoot = new Node();
        if (width >= 16)
            _table = new Slot[1 << 16];
    }

    /// <summary>Number of stored prefixes.</summary>
    public long Count { get; private set; }

    /// <summary>Changes with every published change, so callers can validate cached results with one comparison.</summary>
    public long Version => Volatile.Read(ref _version);

    private long _version;

    /// <summary>Returns the array elements and nodes copied since the last call. For measurements only.</summary>
    public long TakeWork()
    {
        var work = _work;
        _work = 0;
        return work;
    }

    // ---- reading ---------------------------------------------------------------------------------------------------

    /// <summary>Finds the longest stored prefix that contains <paramref name="key" />.</summary>
    public bool TryGetMatch(in TKey key, out TPrefix prefix, out TValue value)
    {
        ref var entry = ref Find(in key);
        if (Unsafe.IsNullRef(ref entry))
        {
            prefix = default!;
            value = default!;
            return false;
        }

        prefix = entry.Prefix;
        value = entry.Value;
        return true;
    }

    private ref Entry Find(in TKey key)
    {
        if (!Prune && !SlotBest)
            return ref FindPlain(in key);

        var nodes = new NodeStack();
        var depths = new DepthStack();
        var count = 0;
        var node = Volatile.Read(ref _root);
        var depth = 0;
        Hit? above = null;

        var table = _table;
        if (table != null)
        {
            ref var slot = ref table[TKey.GetTopBits(key, 16)];
            var depth1 = slot.Depth1;
            var depth2 = slot.Depth2;
            if (depth1 != null)
            {
                if (SlotBest)
                {
                    // the levels above the starting node are summarized by the slot
                    above = slot.Best;
                }
                else if (!Prune || node.HasLocal)
                {
                    nodes[count] = node;
                    depths[count++] = 0;
                }

                node = depth1;
                depth = 1;
                if (depth2 != null)
                {
                    if (!SlotBest && (!Prune || node.HasLocal))
                    {
                        nodes[count] = node;
                        depths[count++] = 1;
                    }

                    node = depth2;
                    depth = 2;
                }
            }
        }

        // descend as far as the key leads, remembering only the nodes that could hold a match
        while (true)
        {
            if (!Prune || node.HasLocal)
            {
                nodes[count] = node;
                depths[count++] = (byte)depth;
            }

            var octet = TKey.GetBits(key, depth << 3, 8);
            if (node.Kids.Test(octet))
            {
                node = node.KidNodes[node.Kids.Rank(octet) - 1];
                depth++;
                continue;
            }

            if (node.Leaves.Test(octet))
            {
                ref var leaf = ref node.LeafEntries[node.Leaves.Rank(octet) - 1];
                if (TKey.MatchesUnderMask(key, leaf.Bits, leaf.Mask))
                    return ref leaf.Entry;
            }

            break;
        }

        // then from the deepest remembered node up
        while (count > 0)
        {
            node = nodes[--count];
            var octet = TKey.GetBits(key, depths[count] << 3, 8);
            if (node.Fringes.Test(octet))
                return ref node.FringeEntries[node.Fringes.Rank(octet) - 1];
            if (node.Prefixes.AndTop(in ArtTables.Ancestors[(octet >> 1) + 128], out var top))
                return ref node.PrefixEntries[node.Prefixes.Rank(top) - 1];
        }

        if (above != null)
            return ref above.Array[above.Index];
        return ref Unsafe.NullRef<Entry>();
    }

    // the version measured before pruning and the precomputed slot matches
    private ref Entry FindPlain(in TKey key)
    {
        var stack = new NodeStack();
        var node = Volatile.Read(ref _root);
        var depth = 0;

        var table = _table;
        if (table != null)
        {
            ref var slot = ref table[TKey.GetTopBits(key, 16)];
            var depth1 = slot.Depth1;
            var depth2 = slot.Depth2;
            if (depth1 != null)
            {
                stack[0] = node;
                node = depth1;
                depth = 1;
                if (depth2 != null)
                {
                    stack[1] = node;
                    node = depth2;
                    depth = 2;
                }
            }
        }

        while (true)
        {
            stack[depth] = node;
            var octet = TKey.GetBits(key, depth << 3, 8);
            if (node.Kids.Test(octet))
            {
                node = node.KidNodes[node.Kids.Rank(octet) - 1];
                depth++;
                continue;
            }

            if (node.Leaves.Test(octet))
            {
                ref var leaf = ref node.LeafEntries[node.Leaves.Rank(octet) - 1];
                if (TKey.MatchesUnderMask(key, leaf.Bits, leaf.Mask))
                    return ref leaf.Entry;
            }

            break;
        }

        while (true)
        {
            var octet = TKey.GetBits(key, depth << 3, 8);
            if (node.Fringes.Test(octet))
                return ref node.FringeEntries[node.Fringes.Rank(octet) - 1];
            if (node.Prefixes.AndTop(in ArtTables.Ancestors[(octet >> 1) + 128], out var top))
                return ref node.PrefixEntries[node.Prefixes.Rank(top) - 1];
            if (depth == 0)
                return ref Unsafe.NullRef<Entry>();
            node = stack[--depth];
        }
    }

    // ---- writing ---------------------------------------------------------------------------------------------------

    private long _countBeforeBulk;

    /// <summary>
    ///     Builds the following changes privately, and publishes them all at once in <see cref="EndBulkLoad" />, or
    ///     discards them in <see cref="AbortBulkLoad" />. Readers keep seeing the table as it was until then, so a bulk
    ///     load is also an atomic batch, like an nftables transaction.
    /// </summary>
    public void BeginBulkLoad()
    {
        _stamp++;
        _inBulkLoad = true;
        _countBeforeBulk = Count;
    }

    /// <summary>Discards every change since <see cref="BeginBulkLoad" />. Readers never saw any of them.</summary>
    public void AbortBulkLoad()
    {
        _writerRoot = _root;
        Count = _countBeforeBulk;
        _inBulkLoad = false;
        _stamp++;
    }

    /// <summary>Trims the arrays of the nodes the bulk load created, then publishes them with one write.</summary>
    public void EndBulkLoad()
    {
        Trim(_writerRoot, _stamp);
        _inBulkLoad = false;
        _stamp++;
        Volatile.Write(ref _root, _writerRoot);
        Interlocked.Increment(ref _version);
        if (_table != null)
            for (var firstOctet = 0; firstOctet < 256; firstOctet++)
                RefreshTable(firstOctet);
    }

    /// <summary>Adds a prefix. Returns false, and changes nothing, when it is already present.</summary>
    public bool TryAdd(in TKey bits, int length, TPrefix prefix, TValue value)
    {
        if ((uint)length > (uint)_width)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (!_inBulkLoad)
            _stamp++;

        var item = new Leaf { Bits = TKey.Truncate(bits, length), Mask = TKey.MaskFor(length), Length = length, Entry = new Entry { Prefix = prefix, Value = value } };
        var path = new NodeStack();
        Span<int> octets = stackalloc int[32];
        var node = _writerRoot;
        var depth = 0;
        Node changed;

        while (true)
        {
            var octet = TKey.GetBits(item.Bits, depth << 3, 8);
            var relativeLength = length - (depth << 3);

            if (relativeLength < 8)
            {
                var index = ArtTables.PrefixToIndex(octet, relativeLength);
                if (node.Prefixes.Test(index))
                    return false;
                changed = Writable(node);
                InsertPrefix(changed, index, item.Entry);
                break;
            }

            if (relativeLength == 8)
            {
                if (node.Fringes.Test(octet))
                    return false;
                changed = Writable(node);
                InsertFringe(changed, octet, item.Entry);
                break;
            }

            if (node.Kids.Test(octet))
            {
                path[depth] = node;
                octets[depth] = octet;
                node = node.KidNodes[node.Kids.Rank(octet) - 1];
                depth++;
                continue;
            }

            if (node.Leaves.Test(octet))
            {
                var existing = node.LeafEntries[node.Leaves.Rank(octet) - 1];
                if (existing.Length == length && TKey.MatchesUnderMask(item.Bits, existing.Bits, existing.Mask))
                    return false;

                // a second prefix below this octet: both move into a new child node
                var child = new Node { Stamp = _stamp };
                AddPrivate(child, depth + 1, in existing);
                AddPrivate(child, depth + 1, in item);
                changed = Writable(node);
                RemoveLeaf(changed, octet);
                InsertKid(changed, octet, child);
                break;
            }

            changed = Writable(node);
            InsertLeaf(changed, octet, in item);
            break;
        }

        changed.Count++;
        if (!_inBulkLoad)
            for (var pathDepth = 0; pathDepth < depth; pathDepth++)
                path[pathDepth].Count++;
        Link(changed, node, ref path, octets, depth);
        if (_inBulkLoad)
            AdjustCounts(in item.Bits, depth, 1);
        UpdateTable(in item.Bits, length, depth);
        if (!_inBulkLoad)
            Interlocked.Increment(ref _version);
        Count++;
        return true;
    }

    /// <summary>Removes a prefix. Returns false when it is not present.</summary>
    public bool TryRemove(in TKey bits, int length)
    {
        if ((uint)length > (uint)_width)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (!_inBulkLoad)
            _stamp++;

        var key = TKey.Truncate(bits, length);
        var path = new NodeStack();
        Span<int> octets = stackalloc int[32];
        var node = _writerRoot;
        var depth = 0;
        Node changed;

        while (true)
        {
            var octet = TKey.GetBits(key, depth << 3, 8);
            var relativeLength = length - (depth << 3);

            if (relativeLength < 8)
            {
                var index = ArtTables.PrefixToIndex(octet, relativeLength);
                if (!node.Prefixes.Test(index))
                    return false;
                changed = Writable(node);
                RemovePrefix(changed, index);
                break;
            }

            if (relativeLength == 8)
            {
                if (!node.Fringes.Test(octet))
                    return false;
                changed = Writable(node);
                RemoveFringe(changed, octet);
                break;
            }

            if (node.Kids.Test(octet))
            {
                path[depth] = node;
                octets[depth] = octet;
                node = node.KidNodes[node.Kids.Rank(octet) - 1];
                depth++;
                continue;
            }

            if (!node.Leaves.Test(octet))
                return false;
            ref var leaf = ref node.LeafEntries[node.Leaves.Rank(octet) - 1];
            if (leaf.Length != length || !TKey.MatchesUnderMask(key, leaf.Bits, leaf.Mask))
                return false;
            changed = Writable(node);
            RemoveLeaf(changed, octet);
            break;
        }

        changed.Count--;

        // keep the shape canonical: a child holding one prefix becomes a leaf in its parent, an empty child goes away,
        // and the parent may then need the same
        var original = node;
        while (depth > 0 && changed.Count <= 1)
        {
            var parent = path[depth - 1];
            var octet = octets[depth - 1];
            var parentCopy = Writable(parent);
            parentCopy.Count--;
            RemoveKid(parentCopy, octet);
            if (changed.Count == 1)
            {
                var remainingLeaf = SingleItem(changed, depth, in key);
                InsertLeaf(parentCopy, octet, in remainingLeaf);
            }

            changed = parentCopy;
            original = parent;
            depth--;
        }

        if (!_inBulkLoad)
            for (var pathDepth = 0; pathDepth < depth; pathDepth++)
                path[pathDepth].Count--;
        Link(changed, original, ref path, octets, depth);
        if (_inBulkLoad)
            AdjustCounts(in key, depth, -1);
        UpdateTable(in key, length, depth);
        if (!_inBulkLoad)
            Interlocked.Increment(ref _version);
        Count--;
        return true;
    }

    // During a bulk load the counts of the nodes above a change are adjusted only after Link has made them private, so
    // that an aborted bulk load leaves the published nodes untouched. A private node's parents are always private.
    private void AdjustCounts(in TKey key, int depth, int delta)
    {
        var node = _writerRoot;
        for (var pathDepth = 0; pathDepth < depth; pathDepth++)
        {
            node.Count += delta;
            node = node.KidNodes[node.Kids.Rank(TKey.GetBits(key, pathDepth << 3, 8)) - 1];
        }
    }

    // after a write that replaced the node at the given depth on the path of key
    private void UpdateTable(in TKey key, int length, int depth)
    {
        if (_table == null || _inBulkLoad || depth > 2)
            return;
        var firstOctet = TKey.GetBits(key, 0, 8);
        if (depth == 2)
        {
            // the levels the slot summarizes did not change, only the depth-2 node
            RefreshSlot(firstOctet, TKey.GetBits(key, 8, 8), Kid(_writerRoot, firstOctet), null, false);
            return;
        }

        // a prefix of the root's own, /0 to /8, covers every /8 block below it
        var blockCount = depth == 0 && length <= 8 ? 1 << (8 - length) : 1;
        for (var blockOffset = 0; blockOffset < blockCount; blockOffset++)
            RefreshTable((firstOctet & ~(blockCount - 1)) + blockOffset);
    }

    private void RefreshTable(int firstOctet)
    {
        var depth1 = Kid(_writerRoot, firstOctet);
        var rootHit = LocalHit(_writerRoot, firstOctet);
        for (var secondOctet = 0; secondOctet < 256; secondOctet++)
            RefreshSlot(firstOctet, secondOctet, depth1, rootHit, true);
    }

    private static Hit? LocalHit(Node node, int octet)
    {
        if (node.Fringes.Test(octet))
            return new Hit(node.FringeEntries, node.Fringes.Rank(octet) - 1);
        if (node.Prefixes.AndTop(in ArtTables.Ancestors[(octet >> 1) + 128], out var top))
            return new Hit(node.PrefixEntries, node.Prefixes.Rank(top) - 1);
        return null;
    }

    private void RefreshSlot(int firstOctet, int secondOctet, Node? depth1, Hit? rootHit, bool refreshBest)
    {
        ref var slot = ref _table![(firstOctet << 8) | secondOctet];
        var depth2 = depth1 == null ? null : Kid(depth1, secondOctet);
        if (refreshBest)
            Volatile.Write(ref slot.Best, (depth1 == null ? null : LocalHit(depth1, secondOctet)) ?? rootHit);

        // a reader that finds a node at depth 2 must also find one at depth 1, so set depth 1 first and clear it last
        if (depth2 == null)
        {
            Volatile.Write(ref slot.Depth2, null);
            Volatile.Write(ref slot.Depth1, depth1);
        }
        else
        {
            Volatile.Write(ref slot.Depth1, depth1);
            Volatile.Write(ref slot.Depth2, depth2);
        }
    }

    private static Node? Kid(Node node, int octet) => node.Kids.Test(octet) ? node.KidNodes[node.Kids.Rank(octet) - 1] : null;

    // a node the current write may change in place: itself if this write created it, otherwise a copy
    private Node Writable(Node node)
    {
        if (node.Stamp == _stamp)
            return node;
        _work++;
        return node.Copy(_stamp);
    }

    // publishes a changed node in place of the original with one reference write
    private void Link(Node changed, Node original, ref NodeStack path, Span<int> octets, int depth)
    {
        while (changed != original)
        {
            if (depth == 0)
            {
                _writerRoot = changed;
                if (!_inBulkLoad)
                    Volatile.Write(ref _root, changed);
                return;
            }

            var parent = path[depth - 1];
            var childIndex = parent.Kids.Rank(octets[depth - 1]) - 1;
            if (_inBulkLoad && parent.Stamp != _stamp)
            {
                // during a bulk load readers must not see any change yet, so the parents are copied up to the root
                var copy = Writable(parent);
                OwnKidArray(copy);
                copy.KidNodes[childIndex] = changed;
                changed = copy;
                original = parent;
                depth--;
                continue;
            }

            // a parent this write or bulk load copied may still share its child array with the published original
            if (parent.Stamp == _stamp)
                OwnKidArray(parent);
            Volatile.Write(ref parent.KidNodes[childIndex], changed);
            return;
        }
    }

    private void OwnKidArray(Node node)
    {
        if ((node.Shared & KidArray) == 0)
            return;
        node.KidNodes = (Node[])node.KidNodes.Clone();
        node.Shared &= unchecked((byte)~KidArray);
        _work += node.KidNodes.Length;
    }

    // adds an item below a node that this write created, changing nodes in place
    private void AddPrivate(Node node, int depth, in Leaf item)
    {
        while (true)
        {
            node.Count++;
            var octet = TKey.GetBits(item.Bits, depth << 3, 8);
            var relativeLength = item.Length - (depth << 3);
            if (relativeLength < 8)
            {
                InsertPrefix(node, ArtTables.PrefixToIndex(octet, relativeLength), item.Entry);
                return;
            }

            if (relativeLength == 8)
            {
                InsertFringe(node, octet, item.Entry);
                return;
            }

            if (node.Kids.Test(octet))
            {
                node = node.KidNodes[node.Kids.Rank(octet) - 1];
                depth++;
                continue;
            }

            if (node.Leaves.Test(octet))
            {
                var existing = node.LeafEntries[node.Leaves.Rank(octet) - 1];
                var child = new Node { Stamp = _stamp };
                AddPrivate(child, depth + 1, in existing);
                RemoveLeaf(node, octet);
                InsertKid(node, octet, child);
                node = child;
                depth++;
                continue;
            }

            InsertLeaf(node, octet, in item);
            return;
        }
    }

    // the one prefix left in a node at the given depth, which lies on the path of key, as a leaf
    private static Leaf SingleItem(Node node, int depth, in TKey key)
    {
        if (!node.Leaves.IsEmpty)
            return node.LeafEntries[0];

        var start = TKey.Truncate(key, depth << 3);
        int length;
        TKey bits;
        Entry entry;
        if (!node.Fringes.IsEmpty)
        {
            bits = TKey.SetBits(start, depth << 3, 8, node.Fringes.First());
            length = (depth << 3) + 8;
            entry = node.FringeEntries[0];
        }
        else
        {
            var index = node.Prefixes.First();
            var relativeLength = 31 - BitOperations.LeadingZeroCount((uint)index);
            bits = relativeLength == 0 ? start : TKey.SetBits(start, depth << 3, relativeLength, index - (1 << relativeLength));
            length = (depth << 3) + relativeLength;
            entry = node.PrefixEntries[0];
        }

        return new Leaf { Bits = bits, Mask = TKey.MaskFor(length), Length = length, Entry = entry };
    }

    // ---- node arrays -----------------------------------------------------------------------------------------------

    private void InsertPrefix(Node node, int index, in Entry entry)
    {
        var count = node.Prefixes.PopCount();
        node.Prefixes.Set(index);
        node.PrefixEntries = InsertAt(node.PrefixEntries, count, node.Prefixes.Rank(index) - 1, entry, ref node.Shared, PrefixArray);
        node.HasLocal = !node.Prefixes.IsEmpty || !node.Fringes.IsEmpty;
    }

    private void RemovePrefix(Node node, int index)
    {
        var position = node.Prefixes.Rank(index) - 1;
        node.PrefixEntries = RemoveAt(node.PrefixEntries, node.Prefixes.PopCount(), position, ref node.Shared, PrefixArray);
        node.Prefixes.Clear(index);
        node.HasLocal = !node.Prefixes.IsEmpty || !node.Fringes.IsEmpty;
    }

    private void InsertFringe(Node node, int octet, in Entry entry)
    {
        var count = node.Fringes.PopCount();
        node.Fringes.Set(octet);
        node.FringeEntries = InsertAt(node.FringeEntries, count, node.Fringes.Rank(octet) - 1, entry, ref node.Shared, FringeArray);
        node.HasLocal = !node.Prefixes.IsEmpty || !node.Fringes.IsEmpty;
    }

    private void RemoveFringe(Node node, int octet)
    {
        var position = node.Fringes.Rank(octet) - 1;
        node.FringeEntries = RemoveAt(node.FringeEntries, node.Fringes.PopCount(), position, ref node.Shared, FringeArray);
        node.Fringes.Clear(octet);
        node.HasLocal = !node.Prefixes.IsEmpty || !node.Fringes.IsEmpty;
    }

    private void InsertKid(Node node, int octet, Node kid)
    {
        var count = node.Kids.PopCount();
        node.Kids.Set(octet);
        node.KidNodes = InsertAt(node.KidNodes, count, node.Kids.Rank(octet) - 1, kid, ref node.Shared, KidArray);
    }

    private void RemoveKid(Node node, int octet)
    {
        var position = node.Kids.Rank(octet) - 1;
        node.KidNodes = RemoveAt(node.KidNodes, node.Kids.PopCount(), position, ref node.Shared, KidArray);
        node.Kids.Clear(octet);
    }

    private void InsertLeaf(Node node, int octet, in Leaf leaf)
    {
        var count = node.Leaves.PopCount();
        node.Leaves.Set(octet);
        node.LeafEntries = InsertAt(node.LeafEntries, count, node.Leaves.Rank(octet) - 1, leaf, ref node.Shared, LeafArray);
    }

    private void RemoveLeaf(Node node, int octet)
    {
        var position = node.Leaves.Rank(octet) - 1;
        node.LeafEntries = RemoveAt(node.LeafEntries, node.Leaves.PopCount(), position, ref node.Shared, LeafArray);
        node.Leaves.Clear(octet);
    }

    // An array still shared with another node, or one that readers can see, is replaced by an exact-size copy. An array
    // that only this writer can see grows by doubling and changes in place.
    private T[] InsertAt<T>(T[] array, int count, int position, in T item, ref byte shared, byte arrayFlag)
    {
        var isShared = (shared & arrayFlag) != 0;
        if (!isShared && count < array.Length)
        {
            Array.Copy(array, position, array, position + 1, count - position);
            array[position] = item;
            return array;
        }

        var result = new T[isShared ? count + 1 : Math.Max(4, count * 2)];
        Array.Copy(array, 0, result, 0, position);
        result[position] = item;
        Array.Copy(array, position, result, position + 1, count - position);
        shared &= (byte)~arrayFlag;
        _work += count + 1;
        return result;
    }

    private T[] RemoveAt<T>(T[] array, int count, int position, ref byte shared, byte arrayFlag)
    {
        if ((shared & arrayFlag) == 0)
        {
            Array.Copy(array, position + 1, array, position, count - position - 1);
            array[count - 1] = default!;
            return array;
        }

        if (count == 1)
        {
            shared &= (byte)~arrayFlag;
            return Array.Empty<T>();
        }

        var result = new T[count - 1];
        Array.Copy(array, 0, result, 0, position);
        Array.Copy(array, position + 1, result, position, count - position - 1);
        shared &= (byte)~arrayFlag;
        _work += count - 1;
        return result;
    }

    // An empty array is shared by every node, so a node that holds one must never change it in place. Marking it
    // shared makes the next insert allocate.
    private static void Trim(Node node, int stamp)
    {
        if (node.Stamp != stamp)
            return;
        node.PrefixEntries = Exact(node.PrefixEntries, node.Prefixes.PopCount());
        node.FringeEntries = Exact(node.FringeEntries, node.Fringes.PopCount());
        node.KidNodes = Exact(node.KidNodes, node.Kids.PopCount());
        node.LeafEntries = Exact(node.LeafEntries, node.Leaves.PopCount());
        node.Shared = AllArrays;
        foreach (var kid in node.KidNodes)
            Trim(kid, stamp);
    }

    private static T[] Exact<T>(T[] array, int count) => array.Length == count ? array : count == 0 ? Array.Empty<T>() : array[..count];

    // ---- diagnostics -----------------------------------------------------------------------------------------------

    /// <summary>The shape of the trie as text: equal text means equal shape. For tests.</summary>
    public string Dump(Func<TKey, int, string> format)
    {
        var text = new StringBuilder();
        Dump(_writerRoot, 0, default, format, text);
        return text.ToString();
    }

    private static void Dump(Node node, int depth, TKey start, Func<TKey, int, string> format, StringBuilder text)
    {
        var indent = new string(' ', depth);
        for (var index = 1; index < 256; index++)
            if (node.Prefixes.Test(index))
            {
                var relativeLength = 31 - BitOperations.LeadingZeroCount((uint)index);
                var bits = relativeLength == 0 ? start : TKey.SetBits(start, depth << 3, relativeLength, index - (1 << relativeLength));
                text.Append(indent).Append("p ").AppendLine(format(bits, (depth << 3) + relativeLength));
            }

        for (var octet = 0; octet < 256; octet++)
        {
            var below = TKey.SetBits(start, depth << 3, 8, octet);
            if (node.Fringes.Test(octet))
                text.Append(indent).Append("f ").AppendLine(format(below, (depth << 3) + 8));
            if (node.Leaves.Test(octet))
            {
                var leaf = node.LeafEntries[node.Leaves.Rank(octet) - 1];
                text.Append(indent).Append("l ").AppendLine(format(leaf.Bits, leaf.Length));
            }

            if (node.Kids.Test(octet))
            {
                var kid = node.KidNodes[node.Kids.Rank(octet) - 1];
                text.Append(indent).Append("n ").Append(format(below, (depth << 3) + 8)).Append(' ').Append(kid.Count).AppendLine();
                Dump(kid, depth + 1, below, format, text);
            }
        }
    }
}

/// <summary>Tables for ART base indexes within one 8-bit stride.</summary>
public static class ArtTables
{
    /// <summary>The base index of a prefix of <paramref name="relativeLength" /> bits, 0 to 7, within a stride.</summary>
    public static int PrefixToIndex(int octet, int relativeLength) => (octet >> (8 - relativeLength)) + (1 << relativeLength);

    /// <summary>Ancestors[i] holds index i and every index above it: i, i/2, i/4, ..., 1.</summary>
    public static readonly BitSet256[] Ancestors = Build();

    private static BitSet256[] Build()
    {
        var table = new BitSet256[256];
        for (var index = 1; index < 256; index++)
            for (var ancestor = index; ancestor > 0; ancestor >>= 1)
                table[index].Set(ancestor);
        return table;
    }
}
