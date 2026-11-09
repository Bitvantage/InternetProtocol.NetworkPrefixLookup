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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GenericTrie;

/// <summary>
///     A production-style version of the optimized core, to test the behaviors the experimental classes skip: removal,
///     index maintenance on every change, and lock-free readers alongside one writer.
/// </summary>
/// <remarks>
///     <para>
///         Everything a lookup reads lives in one <see cref="State" /> snapshot: the node array, prefix lengths, the
///         prefix-and-value array and the multi-level index. A reader reads the snapshot reference once and uses only
///         that snapshot, so it never mixes arrays from two versions.
///     </para>
///     <para>
///         The writer changes the current snapshot in place, but only in ways a reader can tolerate: a new node, value or
///         sub-table is written completely while nothing refers to it, then linked with a single atomic write. When any
///         array is full, the writer copies every array into a larger snapshot and publishes it with one reference write;
///         readers on the old snapshot keep a consistent, slightly older view. Removed nodes, values and sub-tables are
///         never reused in place, because a reader may still be on them; <see cref="Compact" /> reclaims them by building
///         a fresh snapshot.
///     </para>
///     <para>
///         Only one thread may write at a time. Any number of threads may read at any time.
///     </para>
/// </remarks>
public sealed class SnapshotTrie<TKey, TPrefix, TValue> where TKey : struct, IBitKey<TKey>
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Node
    {
        public TKey Bits;
        public TKey Mask;
        public TKey Split;
        public int Child0; // 0 is the sentinel, which never matches
        public int Child1;
        public int Value;  // index into Entries, or -1 for a routing node
    }

    private struct Entry
    {
        public TPrefix Prefix;
        public TValue Value;
    }

    private sealed class State
    {
        public Node[] Nodes = null!;
        public ushort[] Lengths = null!; // cold: prefix length per node, for writers
        public Entry[] Entries = null!;
        public ulong[] Root = null!;     // multi-level index root; empty when there is no index
        public ulong[] SubTables = null!; // sub-tables, concatenated
        public int NodeCount;
        public int ValueCount;
        public int SubEntryCount;        // entries used in SubTables
    }

    private const int Sentinel = 0;
    private const int RootNode = 1;
    private const uint SubTableFlag = 0x8000_0000;

    private readonly int _rootBits;
    private readonly int _subBits;
    private readonly int _threshold;
    private readonly int _keyWidth;

    private State _state;    // what readers see
    private State _writerState; // what the writer works on; the same object as _state except while compacting
    private bool _publishing = true;
    private bool _maintainIndex = true;
    private readonly Stack<int> _scratch = new(); // reused by HasMoreNodesBelow

    /// <summary>Creates an empty trie.</summary>
    /// <param name="keyWidth">The key width in bits, such as 32 for IPv4.</param>
    /// <param name="rootBits">Bits covered by the index root table, or 0 for no index.</param>
    /// <param name="subBits">Bits covered by each sub-table.</param>
    /// <param name="threshold">A block gets a sub-table when more than this many nodes lie below its entry.</param>
    public SnapshotTrie(int keyWidth, int rootBits = 16, int subBits = 8, int threshold = 8)
    {
        _keyWidth = keyWidth;
        _rootBits = rootBits;
        _subBits = subBits;
        _threshold = threshold;
        _writerState = NewState(64, 64, rootBits > 0 ? 1 << subBits : 0);
        InitializeFixedNodes(_writerState);
        if (rootBits > 0)
            RebuildIndex();
        _state = _writerState;
    }

    /// <summary>Number of stored prefixes.</summary>
    public long Count { get; private set; }

    /// <summary>Nodes, values and sub-table entries that removals or index updates left unreachable.</summary>
    public (int Nodes, int Values, int SubEntries) Garbage => (_garbageNodes, _garbageValues, _garbageSubEntries);

    private int _garbageNodes;
    private int _garbageValues;
    private int _garbageSubEntries;

    // ---- reading ---------------------------------------------------------------------------------------------------

    /// <summary>Finds the longest stored prefix that contains <paramref name="key" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(in TKey key, out TPrefix prefix, out TValue value)
    {
        var snapshot = Volatile.Read(ref _state);
        var found = Find(snapshot, key);
        if (found >= 0)
        {
            ref var entry = ref snapshot.Entries[found];
            prefix = entry.Prefix;
            value = entry.Value;
            return true;
        }

        prefix = default!;
        value = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Find(State snapshot, in TKey key)
    {
        ref var nodes = ref MemoryMarshal.GetArrayDataReference(snapshot.Nodes);
        int nodeIndex, found;

        var root = snapshot.Root;
        if (root.Length > 0)
        {
            var entry = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(root), TKey.GetTopBits(key, _rootBits));
            var offset = _rootBits;
            ref var subTables = ref MemoryMarshal.GetArrayDataReference(snapshot.SubTables);
            while (((uint)entry & SubTableFlag) != 0)
            {
                entry = Unsafe.Add(ref subTables, (int)((uint)entry & ~SubTableFlag) + TKey.GetBits(key, offset, _subBits));
                offset += _subBits;
            }

            nodeIndex = (int)(uint)entry;
            found = (int)(entry >> 32);
        }
        else
        {
            nodeIndex = RootNode;
            found = -1;
        }

        while (true)
        {
            ref var node = ref Unsafe.Add(ref nodes, nodeIndex);
            found = SelectBest(node.Value, found);
            var next = ChildAt(ref node, TKey.IsSet(key, node.Split));
            ref var child = ref Unsafe.Add(ref nodes, next);
            if (!TKey.MatchesUnderMask(key, child.Bits, child.Mask))
                return found;
            nodeIndex = next;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ChildAt(ref Node node, bool slot) => Unsafe.Add(ref node.Child0, slot ? 1 : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SelectBest(int value, int best)
    {
        var routing = value >> 31;
        return (best & routing) | (value & ~routing);
    }

    // ---- snapshot management ---------------------------------------------------------------------------------------

    private static State NewState(int nodeCapacity, int valueCapacity, int subTableCapacity) => new()
    {
        Nodes = new Node[nodeCapacity],
        Lengths = new ushort[nodeCapacity],
        Entries = new Entry[valueCapacity],
        Root = Array.Empty<ulong>(),
        SubTables = new ulong[subTableCapacity]
    };

    private static void InitializeFixedNodes(State snapshot)
    {
        // node 0: the sentinel. (key & 0) == bits is false for every key because its bits are not zero.
        snapshot.Nodes[Sentinel] = new Node { Bits = TKey.FromTopBits(1, 1), Value = -1 };
        // node 1: the root, the zero-length prefix
        snapshot.Nodes[RootNode] = new Node { Mask = TKey.MaskFor(0), Split = TKey.SplitFor(0), Value = -1 };
        snapshot.NodeCount = 2;
    }

    // Makes room for the given number of additional nodes, values and sub-table entries, and returns the snapshot to
    // keep writing to. A larger array means a new snapshot. Which other arrays must be copied follows from what refers to
    // what: the index (root and sub-tables) refers to sub-tables and nodes, and nodes refer to nodes and values. An array
    // that refers to a grown array must be copied too; otherwise the writer would put references to new slots into an
    // array that readers of the old snapshot share, and those slots do not exist in their copy.
    //  - Sub-tables full: copy the root and the sub-tables. Nodes, lengths and values are shared.
    //  - Nodes or values full: copy everything except, when they did not grow, the values, which refer to nothing.
    private State Reserve(int nodes, int values, int subEntries)
    {
        var current = _writerState;
        var nodesFull = current.NodeCount + nodes > current.Nodes.Length;
        var valuesFull = current.ValueCount + values > current.Entries.Length;
        var subTablesFull = current.SubEntryCount + subEntries > current.SubTables.Length;
        if (!nodesFull && !valuesFull && !subTablesFull)
            return current;

        static int Grow(int capacity, int needed)
        {
            capacity = Math.Max(capacity, 1);
            while (capacity < needed)
                capacity *= 2;
            return capacity;
        }

        var next = new State
        {
            NodeCount = current.NodeCount,
            ValueCount = current.ValueCount,
            SubEntryCount = current.SubEntryCount,
            Root = (ulong[])current.Root.Clone()
        };

        next.SubTables = new ulong[subTablesFull ? Grow(current.SubTables.Length, current.SubEntryCount + subEntries) : current.SubTables.Length];
        Array.Copy(current.SubTables, next.SubTables, current.SubEntryCount);

        if (nodesFull || valuesFull)
        {
            next.Nodes = new Node[nodesFull ? Grow(current.Nodes.Length, current.NodeCount + nodes) : current.Nodes.Length];
            next.Lengths = new ushort[next.Nodes.Length];
            Array.Copy(current.Nodes, next.Nodes, current.NodeCount);
            Array.Copy(current.Lengths, next.Lengths, current.NodeCount);
        }
        else
        {
            next.Nodes = current.Nodes;
            next.Lengths = current.Lengths;
        }

        if (valuesFull)
        {
            next.Entries = new Entry[Grow(current.Entries.Length, current.ValueCount + values)];
            Array.Copy(current.Entries, next.Entries, current.ValueCount);
        }
        else
            next.Entries = current.Entries;

        _writerState = next;
        if (_publishing)
            Volatile.Write(ref _state, next);
        return next;
    }

    // ---- writing ---------------------------------------------------------------------------------------------------

    /// <summary>Adds a prefix. Returns false when the prefix is already stored.</summary>
    public bool TryAdd(in TKey key, int length, TPrefix prefix, TValue value)
    {
        // at most two new nodes (the value node and a routing node) and one value
        var snapshot = Reserve(2, 1, 0);
        var bits = TKey.Truncate(key, length);

        var parent = RootNode;
        while (true)
        {
            var next = ChildAt(ref snapshot.Nodes[parent], TKey.IsSet(bits, snapshot.Nodes[parent].Split));
            if (next == Sentinel || snapshot.Lengths[next] > length || !TKey.MatchesUnderMask(bits, snapshot.Nodes[next].Bits, snapshot.Nodes[next].Mask))
                break;
            parent = next;
        }

        int changed; // the shortest node whose prefix changed; the index is refreshed over its range

        if (snapshot.Lengths[parent] == length)
        {
            if (snapshot.Nodes[parent].Value >= 0)
                return false;

            // routing node becomes a value node: write the value first, then publish its index with one write
            var valueIndex = NewValue(snapshot, prefix, value);
            Volatile.Write(ref snapshot.Nodes[parent].Value, valueIndex);
            changed = parent;
        }
        else
        {
            var slot = TKey.IsSet(bits, snapshot.Nodes[parent].Split);
            var existing = ChildAt(ref snapshot.Nodes[parent], slot);
            var node = NewNode(snapshot, bits, length);
            snapshot.Nodes[node].Value = NewValue(snapshot, prefix, value);

            if (existing == Sentinel)
            {
                Link(snapshot, parent, slot, node);
                changed = node;
            }
            else if (length < snapshot.Lengths[existing] && TKey.MatchesUnderMask(snapshot.Nodes[existing].Bits, bits, snapshot.Nodes[node].Mask))
            {
                SetChildUnpublished(snapshot, node, TKey.IsSet(snapshot.Nodes[existing].Bits, snapshot.Nodes[node].Split), existing);
                Link(snapshot, parent, slot, node);
                changed = node;
            }
            else
            {
                var commonLength = TKey.CommonPrefixLength(bits, snapshot.Nodes[existing].Bits, Math.Min(length, snapshot.Lengths[existing]));
                var routing = NewNode(snapshot, TKey.Truncate(bits, commonLength), commonLength);
                SetChildUnpublished(snapshot, routing, TKey.IsSet(bits, snapshot.Nodes[routing].Split), node);
                SetChildUnpublished(snapshot, routing, TKey.IsSet(snapshot.Nodes[existing].Bits, snapshot.Nodes[routing].Split), existing);
                Link(snapshot, parent, slot, routing);
                changed = routing;
            }
        }

        Count++;
        RefreshIndex(changed);
        return true;
    }

    /// <summary>Removes exactly <paramref name="length" /> bits of <paramref name="key" />. Returns false when that prefix is not stored.</summary>
    public bool TryRemove(in TKey key, int length)
    {
        var snapshot = _writerState;
        var bits = TKey.Truncate(key, length);

        // record the path, with the slot each node occupies in its parent
        Span<int> path = stackalloc int[_keyWidth + 2];
        Span<bool> slots = stackalloc bool[_keyWidth + 2];
        var depth = 0;
        path[depth++] = RootNode;
        while (true)
        {
            var current = path[depth - 1];
            var slot = TKey.IsSet(bits, snapshot.Nodes[current].Split);
            var next = ChildAt(ref snapshot.Nodes[current], slot);
            if (next == Sentinel || snapshot.Lengths[next] > length || !TKey.MatchesUnderMask(bits, snapshot.Nodes[next].Bits, snapshot.Nodes[next].Mask))
                break;
            slots[depth] = slot;
            path[depth++] = next;
        }

        var target = path[depth - 1];
        if (snapshot.Lengths[target] != length || snapshot.Nodes[target].Value < 0)
            return false; // not stored, or only a routing node

        // demote to a routing node with one write; the old value stays in place for readers that already found it
        Volatile.Write(ref snapshot.Nodes[target].Value, -1);
        _garbageValues++;
        var changed = target;

        // prune routing nodes that are no longer needed, walking back towards the root (which is never pruned)
        for (var pathIndex = depth - 1; pathIndex >= 1; pathIndex--)
        {
            var node = path[pathIndex];
            if (snapshot.Nodes[node].Value >= 0)
                break;

            var child0 = snapshot.Nodes[node].Child0;
            var child1 = snapshot.Nodes[node].Child1;
            var parent = path[pathIndex - 1];
            if (child0 == Sentinel && child1 == Sentinel)
            {
                Link(snapshot, parent, slots[pathIndex], Sentinel); // unlink a childless routing node, then look at its parent
                _garbageNodes++;
                changed = node;
                continue;
            }

            if (child0 == Sentinel || child1 == Sentinel)
            {
                Link(snapshot, parent, slots[pathIndex], child0 == Sentinel ? child1 : child0); // splice the only child into this node's place
                _garbageNodes++;
                changed = node;
            }

            break;
        }

        Count--;
        RefreshIndex(changed);
        return true;
    }

    private int NewNode(State snapshot, in TKey bits, int length)
    {
        var index = snapshot.NodeCount++;
        snapshot.Nodes[index] = new Node { Bits = bits, Mask = TKey.MaskFor(length), Split = TKey.SplitFor(length), Value = -1 };
        snapshot.Lengths[index] = (ushort)length;
        return index;
    }

    private static int NewValue(State snapshot, TPrefix prefix, TValue value)
    {
        var index = snapshot.ValueCount++;
        snapshot.Entries[index] = new Entry { Prefix = prefix, Value = value };
        return index;
    }

    // for nodes that readers cannot reach yet
    private static void SetChildUnpublished(State snapshot, int parent, bool slot, int child)
    {
        if (slot)
            snapshot.Nodes[parent].Child1 = child;
        else
            snapshot.Nodes[parent].Child0 = child;
    }

    // for nodes that readers can reach: one atomic write, ordered after everything written before it
    private static void Link(State snapshot, int parent, bool slot, int child)
    {
        if (slot)
            Volatile.Write(ref snapshot.Nodes[parent].Child1, child);
        else
            Volatile.Write(ref snapshot.Nodes[parent].Child0, child);
    }

    // ---- index -----------------------------------------------------------------------------------------------------

    // Refreshes the index after a change. Only keys inside the changed node's prefix can see a different result, so only
    // index entries whose blocks overlap that prefix can change. The writer walks down the index along the prefix to the
    // deepest entry whose block contains the whole prefix and rebuilds just that entry, or, when the prefix is split
    // across a table's entries, just the entries it covers. Rebuilt entries get fresh sub-tables; the old ones are left
    // for readers that may still be on them and counted as garbage.
    private void RefreshIndex(int changed)
    {
        if (_rootBits == 0 || !_maintainIndex)
            return;

        var snapshot = _writerState;
        var bits = snapshot.Nodes[changed].Bits; // bits past the node's length are zero
        int length = snapshot.Lengths[changed];

        // a short prefix spans several root entries
        if (length < _rootBits)
        {
            var firstTop = TKey.GetTopBits(bits, _rootBits);
            for (var top = firstTop; top < firstTop + (1 << (_rootBits - length)); top++)
                RecomputeEntry(inRoot: true, 0, top, TKey.FromTopBits(top, _rootBits), _rootBits);
            return;
        }

        // descend while the next table's blocks still contain the whole prefix
        var inRoot = true;
        var tableStart = 0;
        var slot = TKey.GetTopBits(bits, _rootBits);
        var entry = snapshot.Root[slot];
        var decided = _rootBits;
        while (((uint)entry & SubTableFlag) != 0 && decided + _subBits <= length)
        {
            inRoot = false;
            tableStart = (int)((uint)entry & ~SubTableFlag);
            slot = TKey.GetBits(bits, decided, _subBits);
            entry = snapshot.SubTables[tableStart + slot];
            decided += _subBits;
        }

        if (((uint)entry & SubTableFlag) == 0)
        {
            // a plain entry covers the whole prefix: rebuild it, possibly with new sub-tables
            RecomputeEntry(inRoot, tableStart, slot, TKey.Truncate(bits, decided), decided);
            return;
        }

        // the entry points to a sub-table whose entries split the prefix: rebuild the entries the prefix covers
        var subTableStart = (int)((uint)entry & ~SubTableFlag);
        var block = TKey.Truncate(bits, decided);
        var firstSubSlot = TKey.GetBits(bits, decided, _subBits);
        for (var subSlot = firstSubSlot; subSlot < firstSubSlot + (1 << (decided + _subBits - length)); subSlot++)
            RecomputeEntry(inRoot: false, subTableStart, subSlot, TKey.SetBits(block, decided, _subBits, subSlot), decided + _subBits);
    }

    private void RecomputeEntry(bool inRoot, int tableStart, int slot, TKey blockKey, int decided)
    {
        var (node, best) = Descend(_writerState, RootNode, -1, blockKey, decided);
        var entry = MakeEntry(node, best, blockKey, decided);
        var snapshot = _writerState; // MakeEntry may have moved to a larger snapshot
        ref var target = ref inRoot ? ref snapshot.Root[slot] : ref snapshot.SubTables[tableStart + slot];
        _garbageSubEntries += SubEntriesBelow(snapshot, target);
        Volatile.Write(ref target, entry);
    }

    private int SubEntriesBelow(State snapshot, ulong entry)
    {
        if (((uint)entry & SubTableFlag) == 0)
            return 0;
        var start = (int)((uint)entry & ~SubTableFlag);
        var total = 1 << _subBits;
        for (var subSlot = 0; subSlot < 1 << _subBits; subSlot++)
            total += SubEntriesBelow(snapshot, snapshot.SubTables[start + subSlot]);
        return total;
    }

    // builds the whole index into _writerState; only called while _writerState is not visible to readers
    private void RebuildIndex()
    {
        _writerState.Root = new ulong[1 << _rootBits];
        for (var top = 0; top < _writerState.Root.Length; top++)
        {
            var key = TKey.FromTopBits(top, _rootBits);
            var (node, best) = Descend(_writerState, RootNode, -1, key, _rootBits);
            var entry = MakeEntry(node, best, key, _rootBits);
            _writerState.Root[top] = entry; // _writerState may be a larger copy by now; Reserve copies the root table
        }
    }

    private ulong MakeEntry(int node, int best, TKey key, int decided)
    {
        var snapshot = _writerState;
        if (decided + _subBits > _keyWidth || !HasMoreNodesBelow(snapshot, node, key, decided))
            return (ulong)(uint)best << 32 | (uint)node;

        var size = 1 << _subBits;
        snapshot = Reserve(0, 0, size);
        var start = snapshot.SubEntryCount;
        snapshot.SubEntryCount += size;
        for (var subSlot = 0; subSlot < size; subSlot++)
        {
            var subKey = TKey.SetBits(key, decided, _subBits, subSlot);
            var (subNode, subBest) = Descend(_writerState, node, best, subKey, decided + _subBits);
            var subEntry = MakeEntry(subNode, subBest, subKey, decided + _subBits);
            _writerState.SubTables[start + subSlot] = subEntry; // not reachable until the parent entry is written
        }

        return (ulong)(uint)best << 32 | SubTableFlag | (uint)start;
    }

    private static (int Node, int Best) Descend(State snapshot, int nodeIndex, int best, in TKey key, int bits)
    {
        while (true)
        {
            best = SelectBest(snapshot.Nodes[nodeIndex].Value, best);
            if (snapshot.Lengths[nodeIndex] >= bits)
                return (nodeIndex, best);

            var next = ChildAt(ref snapshot.Nodes[nodeIndex], TKey.IsSet(key, snapshot.Nodes[nodeIndex].Split));
            if (snapshot.Lengths[next] > bits || !TKey.MatchesUnderMask(key, snapshot.Nodes[next].Bits, snapshot.Nodes[next].Mask))
                return (nodeIndex, best);
            nodeIndex = next;
        }
    }

    private bool HasMoreNodesBelow(State snapshot, int node, in TKey key, int decided)
    {
        var block = TKey.Truncate(key, decided);
        var mask = TKey.MaskFor(decided);
        var count = 0;
        var stack = _scratch;
        stack.Clear();
        if (snapshot.Nodes[node].Child0 != Sentinel)
            stack.Push(snapshot.Nodes[node].Child0);
        if (snapshot.Nodes[node].Child1 != Sentinel)
            stack.Push(snapshot.Nodes[node].Child1);
        while (stack.TryPop(out var child))
        {
            if (!TKey.MatchesUnderMask(snapshot.Nodes[child].Bits, block, mask))
                continue;
            if (++count > _threshold)
                return true;
            if (snapshot.Nodes[child].Child0 != Sentinel)
                stack.Push(snapshot.Nodes[child].Child0);
            if (snapshot.Nodes[child].Child1 != Sentinel)
                stack.Push(snapshot.Nodes[child].Child1);
        }

        return false;
    }

    // ---- bulk loading ----------------------------------------------------------------------------------------------

    /// <summary>
    ///     Stops maintaining the index until <see cref="EndBulkLoad" />. Readers fall back to walking from the root,
    ///     which is slower but always correct, while many prefixes are added quickly.
    /// </summary>
    public void BeginBulkLoad()
    {
        _maintainIndex = false;
        Volatile.Write(ref _writerState.Root, Array.Empty<ulong>());
    }

    /// <summary>Compacts and builds the index once, then resumes maintaining it on every change.</summary>
    public void EndBulkLoad()
    {
        Compact();
        _maintainIndex = true;
    }

    // ---- compaction ------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Builds a fresh snapshot with only reachable nodes and live values, laid out depth-first, with a rebuilt
    ///     index, and publishes it with one write.
    /// </summary>
    public void Compact()
    {
        var source = _writerState;
        var order = new List<int>(source.NodeCount) { Sentinel };
        var stack = new Stack<int>();
        stack.Push(RootNode);
        while (stack.TryPop(out var nodeIndex))
        {
            order.Add(nodeIndex);
            if (source.Nodes[nodeIndex].Child1 != Sentinel)
                stack.Push(source.Nodes[nodeIndex].Child1);
            if (source.Nodes[nodeIndex].Child0 != Sentinel)
                stack.Push(source.Nodes[nodeIndex].Child0);
        }

        var newIndex = new int[source.NodeCount];
        for (var position = 0; position < order.Count; position++)
            newIndex[order[position]] = position;

        var liveValues = 0;
        foreach (var nodeIndex in order)
            if (source.Nodes[nodeIndex].Value >= 0)
                liveValues++;

        var liveSubEntries = source.Root.Length > 0 ? Math.Max(source.SubEntryCount - _garbageSubEntries, 0) : order.Count * 2;
        var next = NewState(Math.Max(order.Count, 2), Math.Max(liveValues, 1), _rootBits > 0 ? Math.Max(1 << _subBits, liveSubEntries + liveSubEntries / 8) : 0);
        var valueCount = 0;
        for (var position = 0; position < order.Count; position++)
        {
            var node = source.Nodes[order[position]];
            node.Child0 = newIndex[node.Child0];
            node.Child1 = newIndex[node.Child1];
            if (node.Value >= 0)
            {
                next.Entries[valueCount] = source.Entries[node.Value];
                node.Value = valueCount++;
            }
            next.Nodes[position] = node;
            next.Lengths[position] = source.Lengths[order[position]];
        }
        next.NodeCount = order.Count;
        next.ValueCount = valueCount;

        // build the index into the new snapshot privately, then publish it with one write
        _publishing = false;
        _writerState = next;
        if (_rootBits > 0)
        {
            RebuildIndex(); // also during a bulk load: EndBulkLoad compacts to build the index
            // the sub-table array grew by doubling; keep an eighth as headroom and give the rest back
            var keptLength = _writerState.SubEntryCount + _writerState.SubEntryCount / 8 + (1 << _subBits);
            if (_writerState.SubTables.Length > keptLength)
                Array.Resize(ref _writerState.SubTables, keptLength);
        }
        _publishing = true;
        Volatile.Write(ref _state, _writerState);

        _garbageNodes = 0;
        _garbageValues = 0;
        _garbageSubEntries = 0;
    }

    // ---- inspection ------------------------------------------------------------------------------------------------

    /// <summary>
    ///     A depth-first text listing of every reachable node: its prefix length, its bits and whether it holds a value.
    ///     Two tries with the same prefixes give the same listing, whatever order the prefixes were added or removed in.
    /// </summary>
    public string Dump(Func<TKey, int, string> format)
    {
        var snapshot = _writerState;
        var text = new System.Text.StringBuilder();
        var stack = new Stack<(int Node, int Depth)>();
        stack.Push((RootNode, 0));
        while (stack.TryPop(out var item))
        {
            var node = snapshot.Nodes[item.Node];
            text.Append(' ', item.Depth).Append(node.Value >= 0 ? "" : "@").AppendLine(format(node.Bits, snapshot.Lengths[item.Node]));
            if (node.Child1 != Sentinel)
                stack.Push((node.Child1, item.Depth + 1));
            if (node.Child0 != Sentinel)
                stack.Push((node.Child0, item.Depth + 1));
        }
        return text.ToString();
    }

    /// <summary>Bytes held by the current snapshot's arrays.</summary>
    public long MemoryInBytes()
    {
        var snapshot = _writerState;
        return (long)snapshot.Nodes.Length * Unsafe.SizeOf<Node>() + (long)snapshot.Lengths.Length * sizeof(ushort)
             + (long)snapshot.Entries.Length * Unsafe.SizeOf<Entry>() + (snapshot.Root.LongLength + snapshot.SubTables.LongLength) * sizeof(ulong);
    }
}
