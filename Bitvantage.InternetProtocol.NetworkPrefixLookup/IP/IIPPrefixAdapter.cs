/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Net;

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

/// <summary>
///     Teaches <see cref="IPPrefixLookup{TPrefix,TAdapter,TValue}" /> to read a prefix type: its address family, network
///     bits and length. Implement it on an empty struct, so that the JIT compiles each lookup for your type.
/// </summary>
/// <remarks>
///     The prefix is read once, when it is added or looked up; later changes to a stored prefix object are not seen. Bits
///     after the length are ignored. <see cref="IPAddressBits" /> converts an <see cref="IPAddress" /> to bits.
/// </remarks>
/// <typeparam name="TPrefix">The prefix type.</typeparam>
public interface IIPPrefixAdapter<in TPrefix>
{
    /// <summary>True for an IPv4 prefix, false for an IPv6 prefix.</summary>
    /// <param name="prefix">The prefix.</param>
    static abstract bool IsIPv4(TPrefix prefix);

    /// <summary>The network address of an IPv4 prefix, with the address's first bit in the most significant bit.</summary>
    /// <param name="prefix">An IPv4 prefix.</param>
    static abstract uint GetIPv4Bits(TPrefix prefix);

    /// <summary>The network address of an IPv6 prefix, with the address's first bit in the most significant bit.</summary>
    /// <param name="prefix">An IPv6 prefix.</param>
    static abstract UInt128 GetIPv6Bits(TPrefix prefix);

    /// <summary>The prefix length: 0 to 32 for IPv4, 0 to 128 for IPv6.</summary>
    /// <param name="prefix">The prefix.</param>
    static abstract int GetLength(TPrefix prefix);
}
