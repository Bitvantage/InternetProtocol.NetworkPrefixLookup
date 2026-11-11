# Bitvantage.InternetProtocol.NetworkPrefixLookup.MaxMind

Loads a whole MaxMind DB file, such as a GeoIP2, GeoLite2 or ASN database, into a lookup table. MaxMind's `Reader` walks
the file's search tree one bit at a time and decodes a record on every lookup. `MaxMindDatabase.Load` decodes every
record once and builds a longest-prefix-match lookup from the networks.

```sh
dotnet add package Bitvantage.InternetProtocol.NetworkPrefixLookup.MaxMind
```

```csharp
using System.Net;
using Bitvantage.InternetProtocol.NetworkPrefixLookup;

var countries = MaxMindDatabase.Load<Dictionary<string, object>>("GeoLite2-Country.mmdb").Freeze();
if (countries.TryGetMatchValue(IPAddress.Parse("81.2.69.160"), out var record))
    Console.WriteLine(((Dictionary<string, object>)record["country"])["iso_code"]); // GB
```

On an AMD Ryzen 7 7800X3D, with a database the size of a full routing table, 1,240,422 IPv4 and 413,309 IPv6 networks:

| | MaxMind `Reader.Find` | Read-write lookup | Frozen lookup |
| --- | --- | --- | --- |
| Time per lookup | 254 ns | 38 ns | 27 ns |
| Allocated per lookup | 344 bytes | Nothing | Nothing |

The lookup costs time to load and memory to hold. That database took 0.9 seconds to load and the read-write lookup
held 225 MB, where the reader opens at once and maps the 19 MB file. The lookup pays off when an application looks up
many addresses, such as a server that checks every request or a job that processes logs. For a few lookups, or where
memory is tight, MaxMind's reader is the better choice.

MaxMind lookups map IPv4-mapped addresses such as `::ffff:81.2.69.160` to IPv4 by default, as MaxMind's reader does.
Set `MaxMindLoadOptions.IncludeAliasedNetworks` to also load the IPv4-compatible, 6to4 and Teredo aliases of MaxMind's
IPv6 databases, so that every address gets the answer that `Reader.Find` gives.

Load only databases you trust. A database's search tree can point many branches at one subtree, so a hostile file of a
few hundred bytes can expand into millions of networks.

The lookups have the same members as the core package's lookups for `System.Net.IPNetwork`, including queries about
networks and frozen tables. The [main README](https://github.com/Bitvantage/InternetProtocol.NetworkPrefixLookup#readme)
describes them.
