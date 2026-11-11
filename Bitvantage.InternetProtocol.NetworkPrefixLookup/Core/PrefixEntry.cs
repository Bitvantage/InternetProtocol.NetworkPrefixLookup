/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) 2024-2026 Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

namespace Bitvantage.InternetProtocol.NetworkPrefixLookup;

/// <summary>A stored prefix and its value.</summary>
/// <typeparam name="TPrefix">The type of the prefix object.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
/// <param name="Prefix">The prefix object that was added.</param>
/// <param name="Value">The value associated with the prefix.</param>
public readonly record struct PrefixEntry<TPrefix, TValue>(TPrefix Prefix, TValue Value);
