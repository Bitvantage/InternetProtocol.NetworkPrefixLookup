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

using System.Net;
using BenchmarkDotNet.Attributes;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

namespace PerformanceTest;

/// <summary>
///     What the IP layer costs on top of the core: one million IPv4 lookups on the real table, against a bare
///     <see cref="BitTrie{TKey,TPrefix,TValue}" /> and through <see cref="IPNetworkLookup{TValue}" />, with nothing else
///     loaded. Set ROUTEVIEWS_DIR.
/// </summary>
public class LayerBenchmarks
{
    private BitTrie<Bits32, IPNetwork, int> _core = null!;
    private IPNetworkLookup<int> _lookup = null!;
    private Bits32[] _keys = null!;
    private uint[] _bits = null!;

    [GlobalSetup]
    public void Setup()
    {
        var table = RouteViews.IPv4;
        _core = new BitTrie<Bits32, IPNetwork, int>();
        using (var batch = _core.BeginBatch())
        {
            for (var networkIndex = 0; networkIndex < table.Networks.Length; networkIndex++)
                _core.TryAdd(new Bits32(IPAddressBits.ToIPv4Bits(table.Networks[networkIndex].BaseAddress)), table.Networks[networkIndex].PrefixLength, table.Networks[networkIndex], table.Asns[networkIndex]);
            batch.Commit();
        }

        _lookup = RouteViews.Build(table);
        _bits = table.IPv4Keys;
        _keys = _bits.Select(keyBits => new Bits32(keyBits)).ToArray();
    }

    [Benchmark(Baseline = true)]
    public long Core()
    {
        var trie = _core;
        long sum = 0;
        foreach (var key in _keys)
            sum += trie.TryGetMatch(in key, out var network, out var asn) ? network.PrefixLength + asn : -1;
        return sum;
    }

    [Benchmark]
    public long IPLayer()
    {
        var lookup = _lookup;
        long sum = 0;
        foreach (var key in _bits)
            sum += lookup.TryGetMatchIPv4(key, out var network, out var asn) ? network.PrefixLength + asn : -1;
        return sum;
    }
}
