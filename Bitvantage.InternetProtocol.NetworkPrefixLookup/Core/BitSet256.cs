/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

/// <summary>A fixed set of 256 bits, with rank and highest-common-bit operations.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BitSet256
{
    public ulong Word0, Word1, Word2, Word3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Test(int bit) => ((Unsafe.Add(ref Unsafe.AsRef(in Word0), bit >> 6) >> (bit & 63)) & 1) != 0;

    public void Set(int bit) => Unsafe.Add(ref Word0, bit >> 6) |= 1UL << (bit & 63);

    public void Clear(int bit) => Unsafe.Add(ref Word0, bit >> 6) &= ~(1UL << (bit & 63));

    public readonly bool IsEmpty => (Word0 | Word1 | Word2 | Word3) == 0;

    public readonly int PopCount() =>
        BitOperations.PopCount(Word0) + BitOperations.PopCount(Word1) + BitOperations.PopCount(Word2) + BitOperations.PopCount(Word3);

    /// <summary>The number of set bits at or below <paramref name="bit" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Rank(int bit)
    {
        ref var masksForBit = ref RankMasks[bit * 4];
        return BitOperations.PopCount(Word0 & masksForBit) + BitOperations.PopCount(Word1 & Unsafe.Add(ref masksForBit, 1))
             + BitOperations.PopCount(Word2 & Unsafe.Add(ref masksForBit, 2)) + BitOperations.PopCount(Word3 & Unsafe.Add(ref masksForBit, 3));
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

    /// <summary>The lowest set bit, or 256 when the set is empty.</summary>
    public readonly int First() =>
        Word0 != 0 ? BitOperations.TrailingZeroCount(Word0) : Word1 != 0 ? 64 + BitOperations.TrailingZeroCount(Word1)
        : Word2 != 0 ? 128 + BitOperations.TrailingZeroCount(Word2) : Word3 != 0 ? 192 + BitOperations.TrailingZeroCount(Word3) : 256;

    /// <summary>The lowest set bit at or above <paramref name="from" />, or 256 when there is none.</summary>
    public readonly int NextSet(int from)
    {
        if (from >= 256)
            return 256;
        var wordIndex = from >> 6;
        var word = Unsafe.Add(ref Unsafe.AsRef(in Word0), wordIndex) & (ulong.MaxValue << (from & 63));
        while (word == 0)
        {
            if (++wordIndex == 4)
                return 256;
            word = Unsafe.Add(ref Unsafe.AsRef(in Word0), wordIndex);
        }

        return (wordIndex << 6) + BitOperations.TrailingZeroCount(word);
    }

    /// <summary>True when a bit is set in both sets.</summary>
    public readonly bool Intersects(in BitSet256 other) => ((Word0 & other.Word0) | (Word1 & other.Word1) | (Word2 & other.Word2) | (Word3 & other.Word3)) != 0;

    /// <summary>The bits set in either set.</summary>
    public static BitSet256 Or(in BitSet256 left, in BitSet256 right) => new() { Word0 = left.Word0 | right.Word0, Word1 = left.Word1 | right.Word1, Word2 = left.Word2 | right.Word2, Word3 = left.Word3 | right.Word3 };

    /// <summary>The bits set in both sets.</summary>
    public static BitSet256 And(in BitSet256 left, in BitSet256 right) => new() { Word0 = left.Word0 & right.Word0, Word1 = left.Word1 & right.Word1, Word2 = left.Word2 & right.Word2, Word3 = left.Word3 & right.Word3 };

    /// <summary>A set with all 256 bits set.</summary>
    public static readonly BitSet256 All = new() { Word0 = ulong.MaxValue, Word1 = ulong.MaxValue, Word2 = ulong.MaxValue, Word3 = ulong.MaxValue };

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

/// <summary>
///     Knuth's ART base indexes for one 8-bit stride: the prefixes of relative length 0 to 7 inside a stride form a
///     complete binary tree numbered 1 to 255, where index i has children 2i and 2i + 1.
/// </summary>
internal static class ArtTables
{
    /// <summary>The base index of a prefix of <paramref name="relativeLength" /> bits, 0 to 7, inside a stride.</summary>
    public static int PrefixToIndex(int octet, int relativeLength) => (octet >> (8 - relativeLength)) + (1 << relativeLength);

    /// <summary>The base index of the longest prefix inside a stride that contains <paramref name="octet" />: relative length 7.</summary>
    public static int OctetToIndex(int octet) => (octet >> 1) + 128;

    /// <summary>The relative length of a base index.</summary>
    public static int IndexToLength(int index) => 31 - BitOperations.LeadingZeroCount((uint)index);

    /// <summary>Ancestors[i] holds index i and every index above it: i, i/2, i/4, ..., 1.</summary>
    public static readonly BitSet256[] Ancestors = BuildAncestors();

    /// <summary>
    ///     Descendants[i] holds index i and every index below it: the prefixes inside the stride that lie inside prefix i.
    ///     For the queries about the prefixes inside a prefix.
    /// </summary>
    public static readonly BitSet256[] Descendants = BuildDescendants();

    /// <summary>
    ///     Octets[i] holds the octets that base index i covers, at which the fringes, leaves and child nodes that lie inside
    ///     prefix i are stored. For the queries about the prefixes inside a prefix.
    /// </summary>
    public static readonly BitSet256[] Octets = BuildOctets();

    /// <summary>StartOctet[i] is the first octet that base index i covers.</summary>
    public static readonly byte[] StartOctet = BuildStartOctets();

    /// <summary>The octets at which the prefixes with the given base indexes start.</summary>
    public static BitSet256 StartOctets(in BitSet256 indexes)
    {
        var octets = default(BitSet256);
        for (var wordIndex = 0; wordIndex < 4; wordIndex++)
            for (var word = Unsafe.Add(ref Unsafe.AsRef(in indexes.Word0), wordIndex); word != 0; word &= word - 1)
                octets.Set(StartOctet[(wordIndex << 6) + BitOperations.TrailingZeroCount(word)]);
        return octets;
    }

    private static byte[] BuildStartOctets()
    {
        var table = new byte[256];
        for (var baseIndex = 1; baseIndex < 256; baseIndex++)
        {
            var length = IndexToLength(baseIndex);
            table[baseIndex] = (byte)((baseIndex - (1 << length)) << (8 - length));
        }

        return table;
    }

    private static BitSet256[] BuildDescendants()
    {
        // each index is a descendant of every index in its own Ancestors set
        var table = new BitSet256[256];
        for (var baseIndex = 1; baseIndex < 256; baseIndex++)
            for (var ancestor = baseIndex; ancestor > 0; ancestor >>= 1)
                table[ancestor].Set(baseIndex);
        return table;
    }

    private static BitSet256[] BuildOctets()
    {
        var table = new BitSet256[256];
        for (var baseIndex = 1; baseIndex < 256; baseIndex++)
        {
            var start = StartOctet(baseIndex);
            for (var octet = start; octet < start + (256 >> IndexToLength(baseIndex)); octet++)
                table[baseIndex].Set(octet);
        }

        return table;

        static int StartOctet(int index) => (index - (1 << IndexToLength(index))) << (8 - IndexToLength(index));
    }

    private static BitSet256[] BuildAncestors()
    {
        var table = new BitSet256[256];
        for (var baseIndex = 1; baseIndex < 256; baseIndex++)
            for (var ancestor = baseIndex; ancestor > 0; ancestor >>= 1)
                table[baseIndex].Set(ancestor);
        return table;
    }
}
