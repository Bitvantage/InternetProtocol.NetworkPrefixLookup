/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

#if !NET9_0_OR_GREATER
namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

// System.Threading.Lock arrived in .NET 9. On .NET 8 the writer lock is a monitor behind the same EnterScope pattern,
// so the trie's code is the same on both.
internal sealed class Lock
{
    public bool IsHeldByCurrentThread => Monitor.IsEntered(this);

    public void Enter() => Monitor.Enter(this);

    public void Exit() => Monitor.Exit(this);

    public Scope EnterScope()
    {
        Monitor.Enter(this);
        return new Scope(this);
    }

    public readonly ref struct Scope(Lock owner)
    {
        public void Dispose() => Monitor.Exit(owner);
    }
}
#endif
