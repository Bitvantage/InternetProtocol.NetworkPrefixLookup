/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

/// <summary>
///     A read-write set of IPv4 and IPv6 prefixes of type <typeparamref name="TPrefix" />, each with a value, with fast
///     longest-prefix-match lookups. <typeparamref name="TAdapter" /> tells it how to read the prefix type.
/// </summary>
/// <remarks>
///     <para>
///         Each address family has its own <see cref="BitTrie{TKey,TPrefix,TValue}" />, with the same guarantees: any number
///         of threads may read while one writes, reads take no locks, and every write does a bounded amount of work.
///     </para>
///     <para>
///         For a table that is loaded once and then only read, <see cref="Freeze(FrozenBitTrieOptions?)" /> builds a copy with faster lookups.
///     </para>
/// </remarks>
/// <typeparam name="TPrefix">The prefix type.</typeparam>
/// <typeparam name="TAdapter">Reads the prefix type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public class IPPrefixLookup<TPrefix, TAdapter, TValue> : IReadOnlyIPPrefixLookup<TPrefix, TValue>
    where TAdapter : struct, IIPPrefixAdapter<TPrefix>
{
    private readonly BitTrie<Bits32, TPrefix, TValue> _ipv4Trie;
    private readonly BitTrie<Bits128, TPrefix, TValue> _ipv6Trie;
    private readonly IPLookupReader<TPrefix, TAdapter, TValue, ReadWriteForm> _reader;

    /// <summary>Creates an empty lookup.</summary>
    /// <param name="options">Lookup options; by default every address is looked up as given.</param>
    public IPPrefixLookup(IPLookupOptions? options = null) : this(new BitTrie<Bits32, TPrefix, TValue>(), new BitTrie<Bits128, TPrefix, TValue>(), options)
    {
    }

    private IPPrefixLookup(BitTrie<Bits32, TPrefix, TValue> ipv4Trie, BitTrie<Bits128, TPrefix, TValue> ipv6Trie, IPLookupOptions? options)
    {
        _ipv4Trie = ipv4Trie;
        _ipv6Trie = ipv6Trie;
        Options = options ?? new IPLookupOptions();
        _reader = new(ipv4Trie, ipv6Trie, Options.MapIPv4MappedAddresses);
    }

    /// <summary>Creates a lookup holding the given prefixes, built in one pass.</summary>
    /// <param name="items">The prefixes, in any order, and their values.</param>
    /// <param name="options">Lookup options; by default every address is looked up as given.</param>
    /// <remarks>
    ///     Faster than adding the prefixes one at a time, and it allocates less: each node is built once, with arrays of
    ///     the exact size. The prefixes are copied into a buffer per address family first, which is garbage afterwards.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     A prefix appears more than once, or <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and a prefix lies
    ///     inside <c>::ffff:0:0/96</c>.
    /// </exception>
    public IPPrefixLookup(IEnumerable<KeyValuePair<TPrefix, TValue>> items, IPLookupOptions? options = null) : this(options) =>
        Load<KeyValuePair<TPrefix, TValue>, Pairs>(items);

    /// <summary>
    ///     Creates a lookup holding the given prefixes, built in one pass: another lookup's contents, or the result of a
    ///     query such as <see cref="GetSubnets" />.
    /// </summary>
    /// <param name="items">The prefixes, in any order, and their values.</param>
    /// <param name="options">Lookup options; by default every address is looked up as given.</param>
    /// <remarks>See <see cref="IPPrefixLookup(IEnumerable{KeyValuePair{TPrefix, TValue}}, IPLookupOptions?)" />.</remarks>
    /// <exception cref="ArgumentException">
    ///     A prefix appears more than once, or <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and a prefix lies
    ///     inside <c>::ffff:0:0/96</c>.
    /// </exception>
    public IPPrefixLookup(IEnumerable<PrefixEntry<TPrefix, TValue>> items, IPLookupOptions? options = null) : this(options) =>
        Load<PrefixEntry<TPrefix, TValue>, Entries>(items);

    // The two shapes of item the constructors take. The members are static, so the JIT compiles Load once for each shape
    // with the reads inlined, instead of calling a delegate for every prefix.
    private interface IItemShape<TItem>
    {
        static abstract TPrefix Prefix(in TItem item);
        static abstract TValue Value(in TItem item);
    }

    private readonly struct Pairs : IItemShape<KeyValuePair<TPrefix, TValue>>
    {
        public static TPrefix Prefix(in KeyValuePair<TPrefix, TValue> item) => item.Key;
        public static TValue Value(in KeyValuePair<TPrefix, TValue> item) => item.Value;
    }

    private readonly struct Entries : IItemShape<PrefixEntry<TPrefix, TValue>>
    {
        public static TPrefix Prefix(in PrefixEntry<TPrefix, TValue> item) => item.Prefix;
        public static TValue Value(in PrefixEntry<TPrefix, TValue> item) => item.Value;
    }

    private void Load<TItem, TShape>(IEnumerable<TItem> items) where TShape : IItemShape<TItem>
    {
        ArgumentNullException.ThrowIfNull(items);

        // a collection can be read twice, so each family's buffer gets its exact size instead of growing by doubling
        var ipv4PrefixCount = 0;
        var ipv6PrefixCount = 0;
        if (items is ICollection<TItem> or IReadOnlyCollection<TItem>)
            foreach (var item in items)
            {
                var prefix = TShape.Prefix(in item);
                IPAddressBits.ThrowIfNull(prefix);
                if (TAdapter.IsIPv4(prefix))
                    ipv4PrefixCount++;
                else
                    ipv6PrefixCount++;
            }

        var ipv4Prefixes = new List<(Bits32 Bits, int Length, TPrefix Prefix, TValue Value)>(ipv4PrefixCount);
        var ipv6Prefixes = new List<(Bits128 Bits, int Length, TPrefix Prefix, TValue Value)>(ipv6PrefixCount);
        foreach (var item in items)
        {
            var prefix = TShape.Prefix(in item);
            IPAddressBits.ThrowIfNull(prefix);
            if (TAdapter.IsIPv4(prefix))
                ipv4Prefixes.Add((new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), prefix, TShape.Value(in item)));
            else
                ipv6Prefixes.Add((StoredIPv6Bits(prefix), TAdapter.GetLength(prefix), prefix, TShape.Value(in item)));
        }

        _ipv4Trie.Load(CollectionsMarshal.AsSpan(ipv4Prefixes));
        _ipv6Trie.Load(CollectionsMarshal.AsSpan(ipv6Prefixes));
    }

    /// <summary>
    ///     A number that changes with every published change to either family, so a caller can cache lookup results and
    ///     check that they are still current with one comparison.
    /// </summary>
    /// <remarks>Read it before the lookups whose results it is to vouch for: a value read after them may already count a change they missed.</remarks>
    public long Version => _ipv4Trie.Version + _ipv6Trie.Version;

    // ---- lookups, written once in IPLookupReader -------------------------------------------------------------------

    /// <summary>The lookup's options; a frozen lookup has those of the lookup it was frozen from.</summary>
    public IPLookupOptions Options { get; }

    /// <inheritdoc />
    public int Count => _reader.Count;

    /// <inheritdoc />
    public int IPv4Count => _reader.IPv4Count;

    /// <inheritdoc />
    public int IPv6Count => _reader.IPv6Count;

    /// <inheritdoc />
    public bool TryGetMatch(IPAddress address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatch(address, out prefix, out value);

    /// <inheritdoc />
    public bool TryGetMatch(ReadOnlySpan<byte> address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatch(address, out prefix, out value);

    /// <inheritdoc />
    public bool TryGetMatchIPv4(uint address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatchIPv4(address, out prefix, out value);

    /// <inheritdoc />
    public bool TryGetMatchIPv6(UInt128 address, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatchIPv6(address, out prefix, out value);

    /// <inheritdoc />
    public bool ContainsMatch(IPAddress address) => _reader.ContainsMatch(address);

    /// <inheritdoc />
    public bool ContainsMatch(ReadOnlySpan<byte> address) => _reader.ContainsMatch(address);

    /// <inheritdoc />
    public bool ContainsMatchIPv4(uint address) => _reader.ContainsMatchIPv4(address);

    /// <inheritdoc />
    public bool ContainsMatchIPv6(UInt128 address) => _reader.ContainsMatchIPv6(address);

    /// <inheritdoc />
    public bool TryGetMatchValue(IPAddress address, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatchValue(address, out value);

    /// <inheritdoc />
    public bool TryGetMatchValue(ReadOnlySpan<byte> address, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatchValue(address, out value);

    /// <inheritdoc />
    public bool TryGetMatchValueIPv4(uint address, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatchValueIPv4(address, out value);

    /// <inheritdoc />
    public bool TryGetMatchValueIPv6(UInt128 address, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatchValueIPv6(address, out value);

    /// <inheritdoc />
    public bool TryGetMatch(TPrefix network, [MaybeNullWhen(false)] out TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetMatch(network, out prefix, out value);

    /// <inheritdoc />
    public bool TryGetValue(TPrefix prefix, [MaybeNullWhen(false)] out TValue value) => _reader.TryGetValue(prefix, out value);

    /// <inheritdoc />
    public bool ContainsPrefix(TPrefix prefix) => _reader.ContainsPrefix(prefix);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(IPAddress address) => _reader.GetMatches(address);

    /// <inheritdoc />
    public int GetMatches(IPAddress address, Span<PrefixEntry<TPrefix, TValue>> destination) => _reader.GetMatches(address, destination);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(ReadOnlySpan<byte> address) => _reader.GetMatches(address);

    /// <inheritdoc />
    public int GetMatches(ReadOnlySpan<byte> address, Span<PrefixEntry<TPrefix, TValue>> destination) => _reader.GetMatches(address, destination);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatchesIPv4(uint address) => _reader.GetMatchesIPv4(address);

    /// <inheritdoc />
    public int GetMatchesIPv4(uint address, Span<PrefixEntry<TPrefix, TValue>> destination) => _reader.GetMatchesIPv4(address, destination);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatchesIPv6(UInt128 address) => _reader.GetMatchesIPv6(address);

    /// <inheritdoc />
    public int GetMatchesIPv6(UInt128 address, Span<PrefixEntry<TPrefix, TValue>> destination) => _reader.GetMatchesIPv6(address, destination);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry<TPrefix, TValue>> GetMatches(TPrefix network) => _reader.GetMatches(network);

    /// <inheritdoc />
    public int GetMatches(TPrefix network, Span<PrefixEntry<TPrefix, TValue>> destination) => _reader.GetMatches(network, destination);

    /// <inheritdoc />
    public IEnumerable<PrefixEntry<TPrefix, TValue>> GetSubnets(TPrefix network) => _reader.GetSubnets(network);

    /// <inheritdoc />
    public bool Overlaps(TPrefix network) => _reader.Overlaps(network);

    /// <summary>Returns the IPv4 prefixes and then the IPv6 prefixes, each in address order.</summary>
    public IEnumerator<PrefixEntry<TPrefix, TValue>> GetEnumerator() => _reader.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- writes ----------------------------------------------------------------------------------------------------

    /// <summary>Adds a prefix. Returns false, and changes nothing, when it is already present.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="value">Its value.</param>
    /// <exception cref="ArgumentException">
    ///     <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and the prefix lies inside <c>::ffff:0:0/96</c>, where no
    ///     address lookup would find it.
    /// </exception>
    public bool TryAdd(TPrefix prefix, TValue value)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.TryAdd(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), prefix, value)
            : _ipv6Trie.TryAdd(StoredIPv6Bits(prefix), TAdapter.GetLength(prefix), prefix, value);
    }

    /// <summary>Adds a prefix.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="value">Its value.</param>
    /// <exception cref="ArgumentException">
    ///     The prefix is already present, or <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and the prefix lies
    ///     inside <c>::ffff:0:0/96</c>.
    /// </exception>
    public void Add(TPrefix prefix, TValue value)
    {
        if (!TryAdd(prefix, value))
            throw new ArgumentException($"The prefix {prefix} is already present.", nameof(prefix));
    }

    /// <summary>Adds a prefix, or replaces the stored prefix object and value of one already present.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="value">Its value.</param>
    /// <returns>True when the prefix was added, false when it was replaced.</returns>
    /// <exception cref="ArgumentException">
    ///     <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and the prefix lies inside <c>::ffff:0:0/96</c>, where no
    ///     address lookup would find it.
    /// </exception>
    public bool AddOrUpdate(TPrefix prefix, TValue value)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.AddOrUpdate(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), prefix, value)
            : _ipv6Trie.AddOrUpdate(StoredIPv6Bits(prefix), TAdapter.GetLength(prefix), prefix, value);
    }

    /// <summary>Returns the value of a prefix, adding it with a value from <paramref name="valueFactory" /> if absent.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="valueFactory">Creates the value, called only when the prefix is absent, while other writers wait. It must not write to this lookup.</param>
    /// <exception cref="ArgumentException">
    ///     <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and the prefix lies inside <c>::ffff:0:0/96</c>, where no
    ///     address lookup would find it.
    /// </exception>
    public TValue GetOrAdd(TPrefix prefix, Func<TPrefix, TValue> valueFactory)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.GetOrAdd(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), prefix, valueFactory)
            : _ipv6Trie.GetOrAdd(StoredIPv6Bits(prefix), TAdapter.GetLength(prefix), prefix, valueFactory);
    }

    /// <summary>Removes a prefix and returns its value. Returns false when it is not present.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="value">The value the prefix had.</param>
    public bool Remove(TPrefix prefix, [MaybeNullWhen(false)] out TValue value)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.Remove(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), out value)
            : _ipv6Trie.Remove(new Bits128(TAdapter.GetIPv6Bits(prefix)), TAdapter.GetLength(prefix), out value);
    }

    /// <summary>Removes a prefix only when its value equals <paramref name="expectedValue" />.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="expectedValue">The value the prefix must have, compared with <see cref="EqualityComparer{T}.Default" />.</param>
    /// <returns>True when the prefix was removed; false, with nothing changed, when it is absent or has another value.</returns>
    /// <remarks>
    ///     The check and the removal happen under the writer lock, so no other write lands between them, as with
    ///     <c>ConcurrentDictionary.TryRemove</c>. Inside a batch the check sees the batch's own changes.
    /// </remarks>
    public bool TryRemove(TPrefix prefix, TValue expectedValue)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.TryRemove(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), expectedValue)
            : _ipv6Trie.TryRemove(new Bits128(TAdapter.GetIPv6Bits(prefix)), TAdapter.GetLength(prefix), expectedValue);
    }

    /// <summary>
    ///     Replaces the stored prefix object and value of a prefix only when its value equals <paramref name="expectedValue" />.
    /// </summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    /// <param name="newValue">The value to store.</param>
    /// <param name="expectedValue">The value the prefix must have, compared with <see cref="EqualityComparer{T}.Default" />.</param>
    /// <returns>True when the prefix was updated; false, with nothing changed, when it is absent or has another value.</returns>
    /// <remarks>
    ///     The check and the update happen under the writer lock, so no other write lands between them, as with
    ///     <c>ConcurrentDictionary.TryUpdate</c>. Inside a batch the check sees the batch's own changes.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     <see cref="IPLookupOptions.MapIPv4MappedAddresses" /> is on and the prefix lies inside <c>::ffff:0:0/96</c>.
    /// </exception>
    public bool TryUpdate(TPrefix prefix, TValue newValue, TValue expectedValue)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.TryUpdate(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix), prefix, newValue, expectedValue)
            : _ipv6Trie.TryUpdate(StoredIPv6Bits(prefix), TAdapter.GetLength(prefix), prefix, newValue, expectedValue);
    }

    // The bits of an IPv6 prefix to store. With mapping on, an address lookup sends every IPv4-mapped address to the IPv4
    // trie, so a prefix inside ::ffff:0:0/96 could be stored and listed but never found by address; it is refused, before
    // anything changes, so that a batch it was written in stays sound.
    private Bits128 StoredIPv6Bits(TPrefix prefix)
    {
        var bits = TAdapter.GetIPv6Bits(prefix);
        if (Options.MapIPv4MappedAddresses && TAdapter.GetLength(prefix) >= 96 && bits >> 32 == 0xFFFF)
            throw new ArgumentException(
                $"The prefix {prefix} lies inside ::ffff:0:0/96. With MapIPv4MappedAddresses on, address lookups treat those addresses as IPv4, so no lookup would find it; add the IPv4 prefix instead.",
                nameof(prefix));
        return new Bits128(bits);
    }

    /// <summary>Removes a prefix. Returns false when it is not present.</summary>
    /// <param name="prefix">The prefix; host bits after its length are ignored.</param>
    public bool Remove(TPrefix prefix)
    {
        IPAddressBits.ThrowIfNull(prefix);
        return TAdapter.IsIPv4(prefix)
            ? _ipv4Trie.Remove(new Bits32(TAdapter.GetIPv4Bits(prefix)), TAdapter.GetLength(prefix))
            : _ipv6Trie.Remove(new Bits128(TAdapter.GetIPv6Bits(prefix)), TAdapter.GetLength(prefix));
    }

    /// <summary>Removes every prefix.</summary>
    public void Clear()
    {
        _ipv4Trie.Clear();
        _ipv6Trie.Clear();
    }

    /// <summary>
    ///     Starts a batch: the following changes build privately and stay invisible to readers until
    ///     <see cref="WriteBatch.Commit" />. Disposing the batch without committing it discards them.
    /// </summary>
    /// <remarks>
    ///     Each address family publishes its changes with one reference write when the batch commits, IPv4 first; a reader
    ///     can see the new IPv4 prefixes a moment before the new IPv6 ones. The batch holds the writer locks until it is
    ///     committed or disposed. The locks belong to the thread that started the batch, so it must end on that thread
    ///     and cannot span an <c>await</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A batch is already in progress.</exception>
    public WriteBatch BeginBatch()
    {
        var ipv4Batch = _ipv4Trie.BeginBatch();
        try
        {
            return new WriteBatch(new FamilyBatches(ipv4Batch, _ipv6Trie.BeginBatch()));
        }
        catch
        {
            ipv4Batch.Dispose();
            throw;
        }
    }

    // An IP lookup's batch: one batch on each family's trie. Both are checked before either ends, and IPv4 commits first.
    private sealed class FamilyBatches(WriteBatch ipv4Batch, WriteBatch ipv6Batch) : IBatchOwner
    {
        public void CheckBatchThread()
        {
            ipv4Batch.CheckThread();
            ipv6Batch.CheckThread();
        }

        public bool BatchFailed => ipv4Batch.Failed || ipv6Batch.Failed;

        public void EndBatch(bool commit)
        {
            if (commit && BatchFailed)
            {
                EndBatch(commit: false);
                throw new InvalidOperationException("A write in the batch threw, so the batch was discarded.");
            }

            if (!commit)
            {
                try
                {
                    ipv6Batch.Dispose();
                }
                finally
                {
                    ipv4Batch.Dispose();
                }

                return;
            }

            try
            {
                ipv4Batch.Commit();
            }
            catch
            {
                // nothing is published yet, so the IPv6 changes go too
                ipv6Batch.Dispose();
                throw;
            }

            ipv6Batch.Commit();
        }
    }

    // ---- freezing and diagnostics ----------------------------------------------------------------------------------

    /// <summary>
    ///     Builds a read-only copy with faster lookups. Later changes to this lookup do not affect it. Writes wait while
    ///     the prefixes are copied out, but not while the copy's tables are built; reads never wait.
    /// </summary>
    /// <param name="options">
    ///     The layout of both address families' tables; by default each family's layout is chosen from its data.
    /// </param>
    public FrozenIPPrefixLookup<TPrefix, TAdapter, TValue> Freeze(FrozenBitTrieOptions? options = null) => new(this, options, options);

    /// <summary>Builds a read-only copy with a different layout for each address family. See <see cref="Freeze(FrozenBitTrieOptions?)" />.</summary>
    /// <param name="ipv4Options">The layout of the IPv4 tables.</param>
    /// <param name="ipv6Options">The layout of the IPv6 tables.</param>
    public FrozenIPPrefixLookup<TPrefix, TAdapter, TValue> Freeze(FrozenBitTrieOptions ipv4Options, FrozenBitTrieOptions ipv6Options) => new(this, ipv4Options, ipv6Options);

    // Both families' prefixes are copied under both writer locks, taken in BeginBatch's order, so that no batch commits
    // between the two and the copy is the lookup at one moment. The tables are built after the locks are released.
    internal (FrozenBitTrie<Bits32, TPrefix, TValue> IPv4, FrozenBitTrie<Bits128, TPrefix, TValue> IPv6) FreezeParts(FrozenBitTrieOptions? ipv4Options, FrozenBitTrieOptions? ipv6Options)
    {
        FrozenBitTrie<Bits32, TPrefix, TValue>.Contents ipv4Contents;
        FrozenBitTrie<Bits128, TPrefix, TValue>.Contents ipv6Contents;
        using (_ipv4Trie.WriterLock.EnterScope())
        using (_ipv6Trie.WriterLock.EnterScope())
        {
            ipv4Contents = _ipv4Trie.CopyContents();
            ipv6Contents = _ipv6Trie.CopyContents();
        }

        return (new(ipv4Contents, ipv4Options ?? new FrozenBitTrieOptions()), new(ipv6Contents, ipv6Options ?? new FrozenBitTrieOptions()));
    }

    /// <summary>Counts the nodes and estimates the memory each address family holds.</summary>
    public IPLookupStatistics GetStatistics() => new(_ipv4Trie.GetStatistics(), _ipv6Trie.GetStatistics());

    /// <summary>The structure of one address family's trie as indented text, for people and tests.</summary>
    /// <param name="family"><see cref="AddressFamily.InterNetwork" /> or <see cref="AddressFamily.InterNetworkV6" />.</param>
    public string ToTextTree(AddressFamily family)
    {
        IPAddressBits.CheckFamily(family);
        return family == AddressFamily.InterNetwork ? _ipv4Trie.ToTextTree(IPAddressBits.Format) : _ipv6Trie.ToTextTree(IPAddressBits.Format);
    }

    /// <summary>The structure of one address family's trie in Graphviz DOT format.</summary>
    /// <param name="family"><see cref="AddressFamily.InterNetwork" /> or <see cref="AddressFamily.InterNetworkV6" />.</param>
    public string ToDotTree(AddressFamily family)
    {
        IPAddressBits.CheckFamily(family);
        return family == AddressFamily.InterNetwork ? _ipv4Trie.ToDotTree(IPAddressBits.Format) : _ipv6Trie.ToDotTree(IPAddressBits.Format);
    }
}

/// <summary>The size of each address family's trie in an IP lookup.</summary>
/// <param name="IPv4">The IPv4 trie.</param>
/// <param name="IPv6">The IPv6 trie.</param>
public readonly record struct IPLookupStatistics(BitTrieStatistics IPv4, BitTrieStatistics IPv6);
