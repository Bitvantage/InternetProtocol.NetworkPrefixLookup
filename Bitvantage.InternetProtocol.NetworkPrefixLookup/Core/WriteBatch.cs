/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

/// <summary>
///     A set of changes that readers see all at once, or not at all, started by <c>BeginBatch</c> on a
///     <see cref="BitTrie{TKey,TPrefix,TValue}" /> or on an IP lookup. Every lookup returns this one type.
/// </summary>
/// <remarks>
///     The batch holds the writer lock until it is committed or disposed, so other threads' writes wait. The lock belongs
///     to the thread that started the batch, so the batch must end on that thread and cannot span an <c>await</c>; ending
///     it on another thread throws and leaves it open.
/// </remarks>
public sealed class WriteBatch : IDisposable
{
    private IBatchOwner? _owner; // what the batch was started on, until it ends

    internal WriteBatch(IBatchOwner owner) => _owner = owner;

    /// <summary>Publishes every change in the batch.</summary>
    /// <remarks>
    ///     A trie publishes with one reference write, and an IP lookup with one per address family, IPv4 first, so a
    ///     reader can see the new IPv4 prefixes a moment before the new IPv6 ones. If a write in the batch threw, nothing
    ///     is published, the batch is discarded and Commit throws. Otherwise only running out of memory can make it throw:
    ///     a trie then publishes nothing, and an IP lookup keeps its IPv4 changes published if only the IPv6 ones fail.
    ///     Either way the batch ends.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The batch was already committed or disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The batch was ended on a thread other than the one that started it, which leaves it open; or a write in the
    ///     batch threw, and the batch was discarded.
    /// </exception>
    public void Commit()
    {
        var owner = _owner ?? throw new ObjectDisposedException(nameof(WriteBatch));
        owner.CheckBatchThread();
        _owner = null;
        owner.EndBatch(commit: true);
    }

    /// <summary>Discards the batch's changes unless it was committed.</summary>
    /// <exception cref="InvalidOperationException">
    ///     The batch was ended on a thread other than the one that started it, which leaves it open.
    /// </exception>
    public void Dispose()
    {
        var owner = _owner;
        if (owner == null)
            return;
        owner.CheckBatchThread();
        _owner = null;
        owner.EndBatch(commit: false);
    }

    // for the IP lookups, which end two batches and must check both before ending either
    internal void CheckThread() => _owner?.CheckBatchThread();

    // true when a write in this batch threw, so that committing it will discard it and throw
    internal bool Failed => _owner is { BatchFailed: true };
}

// What a batch was started on: a trie, or an IP lookup's pair of trie batches.
internal interface IBatchOwner
{
    // throws, changing nothing, unless called on the thread that started the batch
    void CheckBatchThread();

    // commits or discards the batch, and ends it either way
    void EndBatch(bool commit);

    // a write in the batch threw, so committing it will discard it and throw
    bool BatchFailed { get; }
}
