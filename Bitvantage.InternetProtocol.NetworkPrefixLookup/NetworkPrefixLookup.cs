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

using System.Collections;
using System.Runtime.CompilerServices;

namespace Bitvantage.InternetProtocol;

public class NetworkPrefixLookup : NetworkPrefixLookupBase<NetworkPrefixKey>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Add(NetworkPrefix prefix)
    {
        Add(new NetworkPrefixKey(prefix));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual bool TryAdd(NetworkPrefix prefix)
    {
        return TryAdd(new NetworkPrefixKey(prefix));
    }
}

public class NetworkPrefixLookup<TValue> : NetworkPrefixLookupBase<NetworkPrefixKeyValuePair<TValue>>, IEnumerable<NetworkPrefixKeyValuePair<TValue>>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Add(NetworkPrefix prefix, TValue? value)
    {
        Add(new NetworkPrefixKeyValuePair<TValue>(prefix, value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual NetworkPrefixKeyValuePair<TValue> GetOrAdd(NetworkPrefix address, Func<NetworkPrefix, TValue> valueFactoryFunc)
    {
        if (TryGetMatch(address, out var networkValuePair))
            return networkValuePair;

        networkValuePair = new NetworkPrefixKeyValuePair<TValue>(address, valueFactoryFunc.Invoke(address));
        TryAdd(networkValuePair);

        return networkValuePair;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual bool TryAdd(NetworkPrefix network, TValue? value)
    {
        return TryAdd(new NetworkPrefixKeyValuePair<TValue>(network, value));
    }

    IEnumerator<NetworkPrefixKeyValuePair<TValue>> IEnumerable<NetworkPrefixKeyValuePair<TValue>>.GetEnumerator()
    {
        throw new NotImplementedException();
    }

    public IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}