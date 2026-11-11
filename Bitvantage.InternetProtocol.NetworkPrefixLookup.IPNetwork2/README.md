# Bitvantage.InternetProtocol.NetworkPrefixLookup.IPNetwork2

Longest-prefix-match lookups over `IPNetwork2` networks from the [IPNetwork2](https://www.nuget.org/packages/IPNetwork2)
package. Given an address, a lookup finds the most specific stored network that contains it and the value stored with
that network.

```sh
dotnet add package Bitvantage.InternetProtocol.NetworkPrefixLookup.IPNetwork2
```

Or from the NuGet Package Manager console:

```
PM> NuGet\Install-Package Bitvantage.InternetProtocol.NetworkPrefixLookup.IPNetwork2
```

```csharp
using System.Net;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

var zones = new IPNetwork2Lookup<string>
{
    { IPNetwork2.Parse("192.168.0.0/16"), "office" },
    { IPNetwork2.Parse("192.168.10.0/24"), "lab" },
};

// the most specific stored network that contains the address
if (zones.TryGetMatch(IPAddress.Parse("192.168.10.7"), out var network, out var zone))
    Console.WriteLine($"{network} -> {zone}"); // 192.168.10.0/24 -> lab

// a read-only copy with faster lookups
var snapshot = zones.Freeze();
```

`IPNetwork2Lookup<TValue>` and its frozen form have the same members as the core package's lookups for
`System.Net.IPNetwork`: lookups from addresses, integers and bytes, queries about networks, batches and frozen tables.
The [main README](https://github.com/Bitvantage/InternetProtocol.NetworkPrefixLookup#readme) describes them and their
performance.
