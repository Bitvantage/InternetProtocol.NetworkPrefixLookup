/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

// The two forms of an IP lookup, as type arguments of IPLookupReader.
internal readonly struct ReadWriteForm
{
}

internal readonly struct FrozenForm
{
}

// The read-only members of IPPrefixLookup and FrozenIPPrefixLookup, written once; each class holds one of these and
// forwards to it. TForm, ReadWriteForm or FrozenForm, tells the JIT which sealed trie type holds each address family, so
// that each form's lookups call their tries directly and inline them. The members are AggressiveInlining, so that each
// one-line forwarder compiles to what a copy of the member would.
internal readonly struct IPLookupReader<TPrefix, TAdapter, TValue, TForm>
    where TAdapter : struct, IIPPrefixAdapter<TPrefix>
    where TForm : struct
{
    // a BitTrie in the read-write form, a FrozenBitTrie in the frozen form
    private readonly IReadOnlyBitTrie<Bits32, TPrefix, TValue> _ipv4Trie;
    private readonly IReadOnlyBitTrie<Bits128, TPrefix, TValue> _ipv6Trie;
    private readonly bool _mapIPv4MappedAddresses;

    public IPLookupReader(IReadOnlyBitTrie<Bits32, TPrefix, TValue> ipv4Trie, IReadOnlyBitTrie<Bits128, TPrefix, TValue> ipv6Trie, bool mapIPv4MappedAddresses)
    {
        _ipv4Trie = ipv4Trie;
        _ipv6Trie = ipv6Trie;
        _mapIPv4MappedAddresses = mapIPv4MappedAddresses;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(IPAddress address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits)
            ? Match4(new Bits32(ipv4Bits), out prefix, out value)
            : Match6(new Bits128(ipv6Bits), out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(ReadOnlySpan<byte> address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits)
            ? Match4(new Bits32(ipv4Bits), out prefix, out value)
            : Match6(new Bits128(ipv6Bits), out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchIPv4(uint address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => Match4(new Bits32(address), out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchIPv6(UInt128 address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits)
            ? Match4(new Bits32(ipv4Bits), out prefix, out value)
            : Match6(new Bits128(address), out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsMatch(IPAddress address) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits) ? Has4(new Bits32(ipv4Bits)) : Has6(new Bits128(ipv6Bits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsMatch(ReadOnlySpan<byte> address) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits) ? Has4(new Bits32(ipv4Bits)) : Has6(new Bits128(ipv6Bits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsMatchIPv4(uint address) => Has4(new Bits32(address));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsMatchIPv6(UInt128 address) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits) ? Has4(new Bits32(ipv4Bits)) : Has6(new Bits128(address));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchValue(IPAddress address, [MaybeNullWhen(false)] out TValue value) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits)
            ? MatchValue4(new Bits32(ipv4Bits), out value)
            : MatchValue6(new Bits128(ipv6Bits), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchValue(ReadOnlySpan<byte> address, [MaybeNullWhen(false)] out TValue value) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits)
            ? MatchValue4(new Bits32(ipv4Bits), out value)
            : MatchValue6(new Bits128(ipv6Bits), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchValueIPv4(uint address, [MaybeNullWhen(false)] out TValue value) => MatchValue4(new Bits32(address), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatchValueIPv6(UInt128 address, [MaybeNullWhen(false)] out TValue value) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits)
            ? MatchValue4(new Bits32(ipv4Bits), out value)
            : MatchValue6(new Bits128(address), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMatch(TPrefix network, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) =>
        IsIPv4(network, out var ipv4Bits, out var ipv6Bits, out var length)
            ? MatchPrefix4(in ipv4Bits, length, out prefix, out value)
            : MatchPrefix6(in ipv6Bits, length, out prefix, out value);

    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(TPrefix network) =>
        IsIPv4(network, out var ipv4Bits, out var ipv6Bits, out var length) ? PrefixMatchList4(in ipv4Bits, length) : PrefixMatchList6(in ipv6Bits, length);

    public int GetMatches(TPrefix network, Span<PrefixEntry<TPrefix, TValue>> destination) =>
        IsIPv4(network, out var ipv4Bits, out var ipv6Bits, out var length) ? PrefixMatches4(in ipv4Bits, length, destination) : PrefixMatches6(in ipv6Bits, length, destination);

    public IEnumerable<PrefixEntry<TPrefix, TValue>> GetSubnets(TPrefix network)
    {
        if (IsIPv4(network, out var ipv4Bits, out var ipv6Bits, out var length))
            return Subnets4(in ipv4Bits, length);

        // a network that contains every mapped address contains, for the mapped lookups, every IPv4 prefix
        var ipv6Subnets = Subnets6(in ipv6Bits, length);
        return _mapIPv4MappedAddresses && IPAddressBits.ContainsIPv4Mapped(ipv6Bits.Value, length) ? _ipv4Trie.Concat(ipv6Subnets) : ipv6Subnets;
    }

    public bool Overlaps(TPrefix network)
    {
        if (IsIPv4(network, out var ipv4Bits, out var ipv6Bits, out var length))
            return Overlaps4(in ipv4Bits, length);
        return Overlaps6(in ipv6Bits, length) || (_mapIPv4MappedAddresses && _ipv4Trie.Count > 0 && IPAddressBits.ContainsIPv4Mapped(ipv6Bits.Value, length));
    }

    // Sorts a network into a family for the queries about coverage, which follow the address lookups: with mapping on,
    // an IPv6 network inside ::ffff:0:0/96 is the IPv4 network it holds, so ::ffff:10.1.0.0/112 is 10.1.0.0/16.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsIPv4(TPrefix network, out Bits32 ipv4Bits, out Bits128 ipv6Bits, out int length)
    {
        IPAddressBits.ThrowIfNull(network);
        length = TAdapter.GetLength(network);
        if (TAdapter.IsIPv4(network))
        {
            ipv4Bits = new Bits32(TAdapter.GetIPv4Bits(network));
            ipv6Bits = default;
            return true;
        }

        var bits = TAdapter.GetIPv6Bits(network);
        ipv6Bits = new Bits128(bits);
        if (_mapIPv4MappedAddresses && length >= 96 && IPAddressBits.IsIPv4Mapped(bits))
        {
            ipv4Bits = new Bits32((uint)bits);
            length -= 96;
            return true;
        }

        ipv4Bits = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TPrefix prefix, [MaybeNullWhen(false)] out TValue value)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? Value4(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), out value)
            : Value6(new Bits128(TAdapter.GetIPv6Bits(prefix)), TAdapter.GetLength(prefix), out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsPrefix(TPrefix prefix)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? Contains4(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix))
            : Contains6(new Bits128(TAdapter.GetIPv6Bits(prefix)), TAdapter.GetLength(prefix));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(IPAddress address) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits) ? MatchList4(new Bits32(ipv4Bits)) : MatchList6(new Bits128(ipv6Bits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMatches(IPAddress address, Span<PrefixEntry<TPrefix, TValue>> destination) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits) ? Matches4(new Bits32(ipv4Bits), destination) : Matches6(new Bits128(ipv6Bits), destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(ReadOnlySpan<byte> address) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits) ? MatchList4(new Bits32(ipv4Bits)) : MatchList6(new Bits128(ipv6Bits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMatches(ReadOnlySpan<byte> address, Span<PrefixEntry<TPrefix, TValue>> destination) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits, out var ipv6Bits) ? Matches4(new Bits32(ipv4Bits), destination) : Matches6(new Bits128(ipv6Bits), destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatchesIPv4(uint address) => MatchList4(new Bits32(address));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMatchesIPv4(uint address, Span<PrefixEntry<TPrefix, TValue>> destination) => Matches4(new Bits32(address), destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatchesIPv6(UInt128 address) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits) ? MatchList4(new Bits32(ipv4Bits)) : MatchList6(new Bits128(address));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMatchesIPv6(UInt128 address, Span<PrefixEntry<TPrefix, TValue>> destination) =>
        IPAddressBits.IsIPv4(address, _mapIPv4MappedAddresses, out var ipv4Bits) ? Matches4(new Bits32(ipv4Bits), destination) : Matches6(new Bits128(address), destination);

    public int Count => _ipv4Trie.Count + _ipv6Trie.Count;

    public int IPv4Count => _ipv4Trie.Count;

    public int IPv6Count => _ipv6Trie.Count;

    public IEnumerator<PrefixEntry<TPrefix, TValue>> GetEnumerator() => _ipv4Trie.Concat(_ipv6Trie).GetEnumerator();

    // ---- calls into the tries --------------------------------------------------------------------------------------

    // Each casts the trie to its form's sealed type. The JIT resolves the typeof test when it compiles the form, so only
    // a direct call remains and the trie's lookup inlines. A call through the interface would do neither.
    private static bool ReadWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => typeof(TForm) == typeof(ReadWriteForm);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Match4(in Bits32 key, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetMatch(in key, out prefix, out value)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetMatch(in key, out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Match6(in Bits128 key, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetMatch(in key, out prefix, out value)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetMatch(in key, out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Has4(in Bits32 key) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).ContainsMatch(in key)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).ContainsMatch(in key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Has6(in Bits128 key) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).ContainsMatch(in key)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).ContainsMatch(in key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MatchValue4(in Bits32 key, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetMatchValue(in key, out value)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetMatchValue(in key, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MatchValue6(in Bits128 key, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetMatchValue(in key, out value)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetMatchValue(in key, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MatchPrefix4(in Bits32 bits, int length, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetMatch(in bits, length, out prefix, out value)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetMatch(in bits, length, out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MatchPrefix6(in Bits128 bits, int length, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetMatch(in bits, length, out prefix, out value)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetMatch(in bits, length, out prefix, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Value4(in Bits32 bits, int length, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetValue(in bits, length, out value)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).TryGetValue(in bits, length, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Value6(in Bits128 bits, int length, [MaybeNullWhen(false)] out TValue value) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetValue(in bits, length, out value)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).TryGetValue(in bits, length, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Contains4(in Bits32 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).ContainsPrefix(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).ContainsPrefix(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Contains6(in Bits128 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).ContainsPrefix(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).ContainsPrefix(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyList<PrefixEntry<TPrefix, TValue>> MatchList4(in Bits32 key) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in key)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyList<PrefixEntry<TPrefix, TValue>> MatchList6(in Bits128 key) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in key)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Matches4(in Bits32 key, Span<PrefixEntry<TPrefix, TValue>> destination) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in key, destination)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in key, destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Matches6(in Bits128 key, Span<PrefixEntry<TPrefix, TValue>> destination) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in key, destination)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in key, destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyList<PrefixEntry<TPrefix, TValue>> PrefixMatchList4(in Bits32 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyList<PrefixEntry<TPrefix, TValue>> PrefixMatchList6(in Bits128 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PrefixMatches4(in Bits32 bits, int length, Span<PrefixEntry<TPrefix, TValue>> destination) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in bits, length, destination)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetMatches(in bits, length, destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PrefixMatches6(in Bits128 bits, int length, Span<PrefixEntry<TPrefix, TValue>> destination) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in bits, length, destination)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetMatches(in bits, length, destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IEnumerable<PrefixEntry<TPrefix, TValue>> Subnets4(in Bits32 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetSubnets(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).GetSubnets(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IEnumerable<PrefixEntry<TPrefix, TValue>> Subnets6(in Bits128 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetSubnets(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).GetSubnets(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Overlaps4(in Bits32 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).Overlaps(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits32, TPrefix, TValue>>(_ipv4Trie).Overlaps(in bits, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Overlaps6(in Bits128 bits, int length) => ReadWrite
        ? Unsafe.As<BitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).Overlaps(in bits, length)
        : Unsafe.As<FrozenBitTrie<Bits128, TPrefix, TValue>>(_ipv6Trie).Overlaps(in bits, length);
}
