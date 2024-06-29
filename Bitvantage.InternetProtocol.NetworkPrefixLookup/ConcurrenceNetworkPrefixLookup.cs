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

namespace Bitvantage.InternetProtocol;

public class ConcurrenceNetworkPrefixLookup : NetworkPrefixLookup
{
    private readonly object _lock = new();

    public override void Add(NetworkPrefix prefix)
    {
        lock (_lock)
            base.Add(prefix);
    }

    public override void Add(NetworkPrefixKey value)
    {
        lock (_lock)
            base.Add(value);
    }

    public override void Clear()
    {
        lock (_lock)
            base.Clear();
    }

    public override void Remove(NetworkPrefix prefix)
    {
        lock (_lock)
            base.Remove(prefix);
    }

    public override bool TryAdd(NetworkPrefix prefix)
    {
        lock (_lock)
            return base.TryAdd(new NetworkPrefixKey(prefix));
    }

    public override bool TryRemove(NetworkPrefix prefix)
    {
        lock (_lock)
            return base.TryRemove(prefix);
    }
}

public class ConcurrenceNetworkPrefixLookup<TValue> : NetworkPrefixLookup<TValue>
{
    private readonly object _lock = new();

    public override void Add(NetworkPrefix network, TValue? value)
    {
        lock (_lock)
            base.Add(network, value);
    }

    public override void Add(NetworkPrefixKeyValuePair<TValue> value)
    {
        lock (_lock)
            base.Add(value);
    }

    public override void Remove(NetworkPrefix network)
    {
        lock (_lock)
            base.Remove(network);
    }

    public override bool TryAdd(NetworkPrefix network, TValue? value)
    {
        lock (_lock)
            return base.TryAdd(new NetworkPrefixKeyValuePair<TValue>(network, value));
    }

    public override bool TryRemove(NetworkPrefix network)
    {
        lock (_lock)
            return base.TryRemove(network);
    }
}