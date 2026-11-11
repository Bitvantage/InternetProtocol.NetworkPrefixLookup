/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Numerics;
using System.Runtime.CompilerServices;

namespace GenericTrie;

/// <summary>
///     Prototype of a key-type-agnostic core. A path-compressed binary trie over left-aligned bit strings whose width is
///     the width of <typeparamref name="TBits" /> (uint = 32, ulong = 64, UInt128 = 128). It never inspects
///     <typeparamref name="TPrefix" />; it only stores it so lookups can return the caller's own prefix object.
///     The node layout and algorithm mirror NetworkPrefixLookupBase so that benchmarks isolate the cost of abstraction.
///     Only insert and longest-prefix match are implemented.
/// </summary>
public sealed class BitTrie<TBits, TPrefix, TValue> where TBits : unmanaged, IBinaryInteger<TBits>
{
    private static readonly int Width = Unsafe.SizeOf<TBits>() * 8;

    private readonly Node _root = new(TBits.Zero, 0);

    public long Count { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TBits MaskFor(int length) => length == 0 ? TBits.Zero : TBits.AllBitsSet << (Width - length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TBits SplitFor(int length) => length >= Width ? TBits.Zero : TBits.One << (Width - 1 - length);

    /// <summary>Adds a prefix given as left-aligned bits and a length. Bits past the length are ignored.</summary>
    public bool TryAdd(TBits bits, int length, TPrefix prefix, TValue value)
    {
        bits &= MaskFor(length);

        // find the deepest node that contains the new prefix and is no longer than it
        var parent = _root;
        while (true)
        {
            var next = parent.Children[parent.GetSlot(bits)];
            if (next == null || next.Length > length || (bits & next.Mask) != next.Bits)
                break;
            parent = next;
        }

        // case 1: exact match; the parent contains the bits, so equal length means equal prefix
        if (parent.Length == length)
        {
            if (parent.HasValue)
                return false;

            parent.Prefix = prefix;
            parent.Value = value;
            parent.HasValue = true;
            Count++;
            return true;
        }

        var slot = parent.GetSlot(bits);
        var existing = parent.Children[slot];
        var node = new Node(bits, length) { Prefix = prefix, Value = value, HasValue = true };

        // case 2: empty slot
        if (existing == null)
        {
            parent.Children[slot] = node;
            Count++;
            return true;
        }

        // case 3: the new prefix contains the existing child
        if (length < existing.Length && (existing.Bits & node.Mask) == bits)
        {
            node.Children[node.GetSlot(existing.Bits)] = existing;
            parent.Children[slot] = node;
            Count++;
            return true;
        }

        // case 4: siblings; the routing node is the longest common prefix, computed with one leading-zero count
        // instead of NetworkPrefix.GetContainingNetwork
        var commonLength = Math.Min(Math.Min(length, existing.Length), int.CreateTruncating(TBits.LeadingZeroCount(bits ^ existing.Bits)));
        var routing = new Node(bits & MaskFor(commonLength), commonLength);
        routing.Children[routing.GetSlot(bits)] = node;
        routing.Children[routing.GetSlot(existing.Bits)] = existing;
        parent.Children[slot] = routing;
        Count++;
        return true;
    }

    /// <summary>Longest-prefix match for a full-width key given as left-aligned bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(TBits key, out TPrefix prefix, out TValue value)
    {
        Node? best = null;
        Node? node = _root;

        do
        {
            if (node.HasValue)
                best = node;

            node = node.Children[node.GetSlot(key)];
        } while (node != null && (key & node.Mask) == node.Bits);

        if (best != null)
        {
            prefix = best.Prefix;
            value = best.Value;
            return true;
        }

        prefix = default!;
        value = default!;
        return false;
    }

    /// <summary>Counts value and routing nodes, for verifying that the shape matches the current library.</summary>
    public (int Value, int Routing) CountNodes()
    {
        int valueNodeCount = 0, routingNodeCount = 0;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.TryPop(out var node))
        {
            if (node.HasValue)
                valueNodeCount++;
            else
                routingNodeCount++;
            foreach (var child in node.Children)
                if (child != null)
                    stack.Push(child);
        }
        return (valueNodeCount, routingNodeCount);
    }

    private sealed class Node
    {
        public readonly TBits Bits;
        public readonly TBits Mask;
        public readonly TBits Split;
        public readonly int Length;
        public readonly Node?[] Children = new Node?[2];
        public bool HasValue;
        public TPrefix Prefix = default!;
        public TValue Value = default!;

        public Node(TBits bits, int length)
        {
            Bits = bits;
            Length = length;
            Mask = MaskFor(length);
            Split = SplitFor(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetSlot(TBits key) => (key & Split) == TBits.Zero ? 0 : 1;
    }
}
