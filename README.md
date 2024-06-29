# Bitvantage.Networking.NetworkLookup
High-performance variable length network prefix lookup

## Installing via NuGet Package Manager
```sh
PM> NuGet\Install-Package Bitvantage.InternetProtocol.NetworkPrefixLookup
```

## Quick Start
```csharp
var prefixLookup = new NetworkPrefixLookup();
prefixLookup.Add("69.0.0.0/8");
prefixLookup.Add("9.213.0.0/21");
prefixLookup.Add("52.137.186.232/30");
prefixLookup.Add("59.168.0.0/13");
prefixLookup.Add("59.168.83.0/24");
prefixLookup.Add("69.169.125.128/30");
prefixLookup.Add("69.15.21.0/25");
prefixLookup.Add("69.248.13.0/26");
prefixLookup.Add("109.114.42.64/28");
prefixLookup.Add("109.195.0.0/17");
```

### Longest Matching Prefix
Routers typically work by finding the best route to the destination address. It does so by searching through its routing table to find the longest, or most specific, prefix to a particular destination.

```csharp
prefixLookup.GetMatch(IPAddress.Parse("69.248.13.12");
```

### All Matching Prefixes
```csharp
prefixLookup.GetMatchs(IPAddress.Parse("69.248.13.12");
```

## Internals

### Variable Stride Trie
Prefixes are organized into a wide, shallow tree structure. The maximum depth of the tree is 33 levels for IPv4 or 129 levels for IPv6. The actual depth largely depends on how closely related the networks are; however for most applications the depth is generally in the single digits. The depth of the tree is directly proportional to lookup performance.

Below is a visualization of a small tree that contains 10 prefixes. The orange node is the root node, the green nodes are container nodes, and the blue nodes are prefix objects and their associated values.

Prefix nodes have between zero and two children, container nodes have exactly two nodes. The child that is contained in the first half of the parent's address space is on the left side, and the child that is contained in the last half of the parent's address space is on the right side. Container nodes are automatically added and removed to maintain the correct parent-child relationship.

![Small Prefix Tree](https://raw.githubusercontent.com/Bitvantage/InternetProtocol.NetworkPrefixLookup/main/Documentation/Media/NetworkPrefixLookup-Tree-Small.svg)

As more prefixes are added to the tree, the tree tends to grow in width far more quickly than depth. Since the depth of the tree is directly proportional to the search performance, it is possible to rapidly search a tree with millions of prefixes while only examining a relatively small number of the nodes.

![Wide Prefix Tree](https://raw.githubusercontent.com/Bitvantage/InternetProtocol.NetworkPrefixLookup/main/Documentation/Media/NetworkPrefixLookup-Tree-Wide.png)

### Tree Visualization 
A Graphviz DOT diagram can generated, and optionally rendered. In order to render the DOT diagram into a SVG you must have [Graphviz](https://graphviz.org/) installed.
```csharp
prefixLookup.ToDotSvgTree(@"c:\temp\graph.svg", IPVersion.IPv4);
```

A text representation of the tree can also be generated.
```csharp
prefixLookup.ToTextTree(IPVersion.IPv4);
```

### Performance
On a AMD Ryzen 7 7800X3D processor single thread longest match prefix lookup speed of a tree with 100,000 prefxies is about 3.38 million lookups per second. As a reference point, a Dictionary used for exact IP matches is can do around 35.5 million lookups per second.

Performance will vary based on many factors.

### Thread Safety
The standard NetworkPrefixLookup class is lock-free and thread-safe for multiple-readers and a single writer. A ConcurrentPrefixNetworkLookup class is provided that uses locks on write operations and should be used where there are concurrent writers. 