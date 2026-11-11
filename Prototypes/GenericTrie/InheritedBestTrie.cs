/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GenericTrie;

/// <summary>
///     Optimization step 7. Like <see cref="PooledTrie{TKey,TPrefix,TValue}" />, but every node stores the best match at or
///     above itself: the value index of the deepest node on the path from the root, including itself, that holds a
///     value. The walk then does no per-step bookkeeping, the answer is the last matched node's field, and index entries
///     need only a node index, so they shrink from 8 bytes to 4. Inserts pay instead: a new value is pushed down to the
///     nodes below it that inherited the old one.
/// </summary>
public sealed class InheritedBestTrie<TKey, TPrefix, TValue> where TKey : struct, IBitKey<TKey>
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Node
    {
        public TKey Bits;
        public TKey Mask;
        public TKey Split;
        public int Child0; // 0 is the sentinel, which never matches
        public int Child1;
        public int Best;   // value index of the deepest value node at or above this one, or -1
    }

    private struct Entry
    {
        public TPrefix Prefix;
        public TValue Value;
    }

    private const int Root = 1;

    private Node[] _nodes = new Node[1024];
    private int[] _lengths = new int[1024];  // cold: prefix length, for inserts
    private int[] _ownValue = new int[1024]; // cold: the node's own value index or -1, for inserts
    private int _nodeCount;

    private Entry[] _entries = new Entry[1024];
    private int _valueCount;

    private int[]? _index;
    private int _indexBits;

    public InheritedBestTrie()
    {
        NewNode(default, 0, -1);
        _nodes[0].Mask = default;
        _nodes[0].Bits = TKey.FromTopBits(1, 1);
        NewNode(default, 0, -1);
    }

    public int NodeCount => _nodeCount;

    private int NewNode(in TKey bits, int length, int best)
    {
        if (_nodeCount == _nodes.Length)
        {
            Array.Resize(ref _nodes, _nodes.Length * 2);
            Array.Resize(ref _lengths, _lengths.Length * 2);
            Array.Resize(ref _ownValue, _ownValue.Length * 2);
        }

        var index = _nodeCount++;
        _nodes[index] = new Node { Bits = bits, Mask = TKey.MaskFor(length), Split = TKey.SplitFor(length), Best = best };
        _lengths[index] = length;
        _ownValue[index] = -1;
        return index;
    }

    private int NewValue(TPrefix prefix, TValue value)
    {
        if (_valueCount == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);

        _entries[_valueCount] = new Entry { Prefix = prefix, Value = value };
        return _valueCount++;
    }

    private void SetChild(int parent, bool slot, int child)
    {
        if (slot)
            _nodes[parent].Child1 = child;
        else
            _nodes[parent].Child0 = child;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ChildAt(ref Node node, bool slot) => Unsafe.Add(ref node.Child0, slot ? 1 : 0);

    // gives node its own value and pushes it down to every descendant that inherited from above
    private void AssignValue(int node, int value)
    {
        _ownValue[node] = value;
        _nodes[node].Best = value;

        var stack = new Stack<int>();
        if (_nodes[node].Child0 != 0)
            stack.Push(_nodes[node].Child0);
        if (_nodes[node].Child1 != 0)
            stack.Push(_nodes[node].Child1);
        while (stack.TryPop(out var descendant))
        {
            if (_ownValue[descendant] >= 0)
                continue; // this subtree inherits from descendant, which is deeper
            _nodes[descendant].Best = value;
            if (_nodes[descendant].Child0 != 0)
                stack.Push(_nodes[descendant].Child0);
            if (_nodes[descendant].Child1 != 0)
                stack.Push(_nodes[descendant].Child1);
        }
    }

    public bool TryAdd(in TKey key, int length, TPrefix prefix, TValue value)
    {
        _index = null;

        var bits = TKey.Truncate(key, length);

        var parent = Root;
        while (true)
        {
            var next = ChildAt(ref _nodes[parent], TKey.IsSet(bits, _nodes[parent].Split));
            if (next == 0 || _lengths[next] > length || !TKey.MatchesUnderMask(bits, _nodes[next].Bits, _nodes[next].Mask))
                break;
            parent = next;
        }

        if (_lengths[parent] == length)
        {
            if (_ownValue[parent] >= 0)
                return false;

            AssignValue(parent, NewValue(prefix, value));
            return true;
        }

        var slot = TKey.IsSet(bits, _nodes[parent].Split);
        var existing = ChildAt(ref _nodes[parent], slot);
        var valueIndex = NewValue(prefix, value);
        var node = NewNode(bits, length, valueIndex);
        _ownValue[node] = valueIndex;

        if (existing == 0)
        {
            SetChild(parent, slot, node);
            return true;
        }

        if (length < _lengths[existing] && TKey.MatchesUnderMask(_nodes[existing].Bits, bits, _nodes[node].Mask))
        {
            SetChild(node, TKey.IsSet(_nodes[existing].Bits, _nodes[node].Split), existing);
            AssignValue(node, valueIndex); // the existing subtree now inherits from the new node
            SetChild(parent, slot, node);
            return true;
        }

        var commonLength = TKey.CommonPrefixLength(bits, _nodes[existing].Bits, Math.Min(length, _lengths[existing]));
        var routing = NewNode(TKey.Truncate(bits, commonLength), commonLength, _nodes[parent].Best);
        SetChild(routing, TKey.IsSet(bits, _nodes[routing].Split), node);
        SetChild(routing, TKey.IsSet(_nodes[existing].Bits, _nodes[routing].Split), existing);
        SetChild(parent, slot, routing);
        return true;
    }

    /// <summary>Reorders the node array depth-first, keeping the sentinel at index 0.</summary>
    public void RelayoutDepthFirst()
    {
        var newIndex = new int[_nodeCount];
        var visitOrder = new int[_nodeCount];
        var count = 0;
        visitOrder[count++] = 0;

        var stack = new Stack<int>();
        stack.Push(Root);
        while (stack.TryPop(out var nodeIndex))
        {
            visitOrder[count++] = nodeIndex;
            if (_nodes[nodeIndex].Child1 != 0)
                stack.Push(_nodes[nodeIndex].Child1);
            if (_nodes[nodeIndex].Child0 != 0)
                stack.Push(_nodes[nodeIndex].Child0);
        }

        for (var position = 0; position < count; position++)
            newIndex[visitOrder[position]] = position;

        var nodes = new Node[_nodes.Length];
        var lengths = new int[_lengths.Length];
        var ownValue = new int[_ownValue.Length];
        for (var position = 0; position < count; position++)
        {
            var node = _nodes[visitOrder[position]];
            node.Child0 = newIndex[node.Child0];
            node.Child1 = newIndex[node.Child1];
            nodes[position] = node;
            lengths[position] = _lengths[visitOrder[position]];
            ownValue[position] = _ownValue[visitOrder[position]];
        }

        _nodes = nodes;
        _lengths = lengths;
        _ownValue = ownValue;
        _index = null;
    }

    /// <summary>Builds a 4-byte-per-entry index: the deepest node decided by the first <paramref name="bits" /> bits.</summary>
    public void BuildIndex(int bits)
    {
        var index = new int[1 << bits];
        for (var top = 0; top < index.Length; top++)
        {
            var key = TKey.FromTopBits(top, bits);
            var nodeIndex = Root;
            while (_lengths[nodeIndex] < bits)
            {
                var next = ChildAt(ref _nodes[nodeIndex], TKey.IsSet(key, _nodes[nodeIndex].Split));
                if (_lengths[next] > bits || !TKey.MatchesUnderMask(key, _nodes[next].Bits, _nodes[next].Mask))
                    break;
                nodeIndex = next;
            }

            index[top] = nodeIndex;
        }

        _indexBits = bits;
        _index = index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindValueIndex(in TKey key)
    {
        ref var nodes = ref MemoryMarshal.GetArrayDataReference(_nodes);

        var index = _index;
        var nodeIndex = index != null ? Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(index), TKey.GetTopBits(key, _indexBits)) : Root;

        ref var node = ref Unsafe.Add(ref nodes, nodeIndex);
        while (true)
        {
            ref var child = ref Unsafe.Add(ref nodes, ChildAt(ref node, TKey.IsSet(key, node.Split)));
            if (!TKey.MatchesUnderMask(key, child.Bits, child.Mask))
                return node.Best;
            node = ref child;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(in TKey key, out TPrefix prefix, out TValue value)
    {
        var best = FindValueIndex(key);
        if (best >= 0)
        {
            ref var entry = ref _entries[best];
            prefix = entry.Prefix;
            value = entry.Value;
            return true;
        }

        prefix = default!;
        value = default!;
        return false;
    }
}
