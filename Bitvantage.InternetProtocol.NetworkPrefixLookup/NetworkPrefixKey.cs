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

// TODO: should these be classes?
public record NetworkPrefixKey()
{
    public NetworkPrefix Prefix { get; internal set; }

    // should there be an empty network type?
    public NetworkPrefixKey(NetworkPrefix prefix) : this()
    {
        Prefix = prefix;
    }
}

public record NetworkPrefixKeyValuePair<TValue>() : NetworkPrefixKey
{
    public TValue? Value { get; }

    // should there be an empty network type?
    public NetworkPrefixKeyValuePair(NetworkPrefix prefix, TValue? value) : this()
    {
        Prefix = prefix;
        Value = value;
    }
}
