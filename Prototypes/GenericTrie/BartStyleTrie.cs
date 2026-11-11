/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GenericTrie;

/// <summary>
///     The design of the current Go BART (github.com/gaissmai/bart), written for this prototype to measure it in .NET:
///     a multibit trie with 8-bit strides. Each node keeps the prefixes that end inside its stride as a complete binary
///     tree (Knuth's ART base index, indexes 1 to 255) in a 256-bit set with a popcount-compressed value array, and its
///     children in a second 256-bit set and array. The longest match inside a node is one AND with a precomputed set of
///     ancestors and a leading-zero count, without backtracking. A prefix that ends exactly on a stride boundary is
///     stored as a fringe child, and a lone longer prefix as a leaf child, so paths are compressed. Insert only.
/// </summary>
public sealed class BartStyleTrie<TKey> where TKey : struct, IBitKey<TKey>
{
    private abstract class Kid;

    private sealed class Node : Kid
    {
        public BitSet256 Prefixes;
        public int[] PrefixValues = Array.Empty<int>();
        public BitSet256 Children;
        public Kid[] ChildArray = Array.Empty<Kid>();
    }

    // a prefix that ends exactly on the stride boundary: it covers every address below its octet
    private sealed class Fringe(int value) : Kid
    {
        public int Value = value;
    }

    // a lone prefix longer than the stride, stored where its subtree would start
    private sealed class Leaf(TKey bits, int length, int value) : Kid
    {
        public readonly TKey Bits = bits;
        public readonly TKey Mask = TKey.MaskFor(length);
        public readonly int Length = length;
        public int Value = value;
    }

    [InlineArray(32)]
    private struct NodeStack
    {
        private Node _element;
    }

    private readonly Node _root = new();
    private readonly int _width;

    public BartStyleTrie(int width) => _width = width;

    public int NodeCount { get; private set; } = 1;

    /// <summary>Adds or replaces a prefix with a value, which must be zero or more.</summary>
    public void Insert(in TKey bits, int length, int value)
    {
        var key = TKey.Truncate(bits, length);
        var node = _root;
        for (var depth = 0; ; depth++)
        {
            var octet = TKey.GetBits(key, depth * 8, 8);
            var relativeLength = length - depth * 8;
            if (relativeLength < 8)
            {
                SetPrefix(node, PrefixToIndex(octet, relativeLength), value);
                return;
            }

            if (!node.Children.Test(octet))
            {
                InsertChild(node, octet, relativeLength == 8 ? new Fringe(value) : new Leaf(key, length, value));
                return;
            }

            var childIndex = node.Children.Rank(octet) - 1;
            switch (node.ChildArray[childIndex])
            {
                case Node child:
                    node = child;
                    continue;

                case Fringe fringe when relativeLength == 8:
                    fringe.Value = value;
                    return;

                case Fringe fringe:
                {
                    // push the fringe down: it becomes the default route of a new node
                    var child = new Node();
                    NodeCount++;
                    SetPrefix(child, 1, fringe.Value);
                    node.ChildArray[childIndex] = child;
                    node = child;
                    continue;
                }

                case Leaf leaf when leaf.Length == length && TKey.MatchesUnderMask(key, leaf.Bits, leaf.Mask):
                    leaf.Value = value;
                    return;

                case Leaf leaf:
                {
                    // push the leaf down into a new node, then keep inserting below
                    var child = new Node();
                    NodeCount++;
                    node.ChildArray[childIndex] = child;
                    InsertBelow(child, depth + 1, leaf.Bits, leaf.Length, leaf.Value);
                    node = child;
                    continue;
                }
            }
        }
    }

    // inserts into the subtree rooted at node, which sits at the given depth
    private void InsertBelow(Node node, int depth, TKey key, int length, int value)
    {
        var octet = TKey.GetBits(key, depth * 8, 8);
        var relativeLength = length - depth * 8;
        if (relativeLength < 8)
            SetPrefix(node, PrefixToIndex(octet, relativeLength), value);
        else
            InsertChild(node, octet, relativeLength == 8 ? new Fringe(value) : new Leaf(key, length, value));
    }

    /// <summary>The value of the longest stored prefix that contains the key, or -1.</summary>
    public int Lookup(in TKey key)
    {
        var stack = new NodeStack();
        var node = _root;
        var depth = 0;
        var lastDepth = _width / 8 - 1;
        for (; ; depth++)
        {
            stack[depth] = node;
            var octet = TKey.GetBits(key, depth * 8, 8);
            if (!node.Children.Test(octet))
                break;

            var kid = node.ChildArray[node.Children.Rank(octet) - 1];
            if (kid is Node child)
            {
                node = child;
                if (depth == lastDepth)
                    break; // cannot happen with fringes, kept for safety
                continue;
            }

            if (kid is Fringe fringe)
                return fringe.Value;

            var leaf = Unsafe.As<Leaf>(kid);
            if (TKey.MatchesUnderMask(key, leaf.Bits, leaf.Mask))
                return leaf.Value;
            break;
        }

        // the deepest node first: the first match found is the longest
        for (; depth >= 0; depth--)
        {
            node = stack[depth];
            if (node.PrefixValues.Length == 0)
                continue;
            var index = (TKey.GetBits(key, depth * 8, 8) >> 1) + 128; // the octet's index at relative length 7
            if (node.Prefixes.AndTop(in AncestorTable[index], out var top))
                return node.PrefixValues[node.Prefixes.Rank(top) - 1];
        }

        return -1;
    }

    /// <summary>Bytes held by the nodes, leaves, fringes and arrays, estimated from their sizes.</summary>
    public long EstimateBytes()
    {
        long bytes = 0;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.TryPop(out var node))
        {
            bytes += 16 + 32 + 8 + 32 + 8; // object header, two sets, two array references
            bytes += 24 + 4L * node.PrefixValues.Length + 24 + 8L * node.ChildArray.Length;
            foreach (var kid in node.ChildArray)
            {
                if (kid is Node child)
                    stack.Push(child);
                else if (kid is Fringe)
                    bytes += 24;
                else
                    bytes += 16 + 3 * Unsafe.SizeOf<TKey>() + 8;
            }
        }
        return bytes;
    }

    private static int PrefixToIndex(int octet, int relativeLength) => (octet >> (8 - relativeLength)) + (1 << relativeLength);

    private static void SetPrefix(Node node, int index, int value)
    {
        if (node.Prefixes.Test(index))
        {
            node.PrefixValues[node.Prefixes.Rank(index) - 1] = value;
            return;
        }

        node.Prefixes.Set(index);
        node.PrefixValues = InsertAt(node.PrefixValues, node.Prefixes.Rank(index) - 1, value);
    }

    private void InsertChild(Node node, int octet, Kid kid)
    {
        node.Children.Set(octet);
        node.ChildArray = InsertAt(node.ChildArray, node.Children.Rank(octet) - 1, kid);
    }

    private static T[] InsertAt<T>(T[] array, int position, T item)
    {
        var result = new T[array.Length + 1];
        Array.Copy(array, 0, result, 0, position);
        result[position] = item;
        Array.Copy(array, position, result, position + 1, array.Length - position);
        return result;
    }

    // AncestorTable[i] holds index i and every index above it in the complete binary tree: i, i/2, i/4, ..., 1
    private static readonly BitSet256[] AncestorTable = BuildAncestorTable();

    private static BitSet256[] BuildAncestorTable()
    {
        var table = new BitSet256[256];
        for (var index = 1; index < 256; index++)
            for (var ancestor = index; ancestor > 0; ancestor >>= 1)
                table[index].Set(ancestor);
        return table;
    }
}

/// <summary>A fixed set of 256 bits, with rank and highest-common-bit operations.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BitSet256
{
    public ulong Word0, Word1, Word2, Word3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Test(int bit) => ((Unsafe.Add(ref Unsafe.AsRef(in Word0), bit >> 6) >> (bit & 63)) & 1) != 0;

    public void Set(int bit) => Unsafe.Add(ref Word0, bit >> 6) |= 1UL << (bit & 63);

    public void Clear(int bit) => Unsafe.Add(ref Word0, bit >> 6) &= ~(1UL << (bit & 63));

    public readonly bool IsEmpty => (Word0 | Word1 | Word2 | Word3) == 0;

    public readonly int PopCount() => BitOperations.PopCount(Word0) + BitOperations.PopCount(Word1) + BitOperations.PopCount(Word2) + BitOperations.PopCount(Word3);

    /// <summary>The lowest set bit, or 256 when the set is empty.</summary>
    public readonly int First() =>
        Word0 != 0 ? BitOperations.TrailingZeroCount(Word0) : Word1 != 0 ? 64 + BitOperations.TrailingZeroCount(Word1)
        : Word2 != 0 ? 128 + BitOperations.TrailingZeroCount(Word2) : Word3 != 0 ? 192 + BitOperations.TrailingZeroCount(Word3) : 256;

    /// <summary>The number of set bits at or below <paramref name="bit" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Rank(int bit)
    {
        ref var wordMasks = ref RankMasks[bit * 4];
        return BitOperations.PopCount(Word0 & wordMasks) + BitOperations.PopCount(Word1 & Unsafe.Add(ref wordMasks, 1))
             + BitOperations.PopCount(Word2 & Unsafe.Add(ref wordMasks, 2)) + BitOperations.PopCount(Word3 & Unsafe.Add(ref wordMasks, 3));
    }

    /// <summary>The highest bit set in both sets, if any.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AndTop(in BitSet256 other, out int top)
    {
        ulong commonBits;
        if ((commonBits = Word3 & other.Word3) != 0) { top = 255 - BitOperations.LeadingZeroCount(commonBits); return true; }
        if ((commonBits = Word2 & other.Word2) != 0) { top = 191 - BitOperations.LeadingZeroCount(commonBits); return true; }
        if ((commonBits = Word1 & other.Word1) != 0) { top = 127 - BitOperations.LeadingZeroCount(commonBits); return true; }
        if ((commonBits = Word0 & other.Word0) != 0) { top = 63 - BitOperations.LeadingZeroCount(commonBits); return true; }
        top = 0;
        return false;
    }

    // RankMasks[bit * 4 + wordIndex]: the bits of word wordIndex at or below bit
    private static readonly ulong[] RankMasks = BuildRankMasks();

    private static ulong[] BuildRankMasks()
    {
        var masks = new ulong[256 * 4];
        for (var bit = 0; bit < 256; bit++)
            for (var wordIndex = 0; wordIndex < 4; wordIndex++)
                masks[bit * 4 + wordIndex] = wordIndex < bit >> 6 ? ulong.MaxValue : wordIndex == bit >> 6 ? ulong.MaxValue >> (63 - (bit & 63)) : 0;
        return masks;
    }
}
