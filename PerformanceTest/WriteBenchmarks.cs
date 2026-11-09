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

/// <summary>Writes to the real IPv4 table: small batches, and single writes. Set ROUTEVIEWS_DIR.</summary>
[MemoryDiagnoser]
public class WriteBenchmarks
{
    private IPNetworkLookup<int> _ipv4Lookup = null!;
    private IPNetwork[] _churn = null!;
    private int[] _churnAsns = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ipv4Lookup = RouteViews.Build(RouteViews.IPv4);
        // every 1,000th prefix of the table, removed and added back
        var table = RouteViews.IPv4;
        _churn = table.Networks.Where((_, networkIndex) => networkIndex % 1000 == 0).ToArray();
        _churnAsns = table.Asns.Where((_, asnIndex) => asnIndex % 1000 == 0).ToArray();
    }

    /// <summary>One batch that removes 10 prefixes and adds them back.</summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public void SmallBatch()
    {
        for (var round = 0; round < 100; round++)
        {
            using var batch = _ipv4Lookup.BeginBatch();
            for (var churnIndex = 0; churnIndex < 10; churnIndex++)
                _ipv4Lookup.Remove(_churn[churnIndex]);
            for (var churnIndex = 0; churnIndex < 10; churnIndex++)
                _ipv4Lookup.Add(_churn[churnIndex], _churnAsns[churnIndex]);
            batch.Commit();
        }
    }

    /// <summary>1,055 prefixes removed and added back, one write at a time.</summary>
    [Benchmark]
    public void SingleWrites()
    {
        for (var churnIndex = 0; churnIndex < _churn.Length; churnIndex++)
            _ipv4Lookup.Remove(_churn[churnIndex]);
        for (var churnIndex = 0; churnIndex < _churn.Length; churnIndex++)
            _ipv4Lookup.Add(_churn[churnIndex], _churnAsns[churnIndex]);
    }
}
