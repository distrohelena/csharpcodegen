#pragma once

/// <summary>
/// Provides lightweight managed-style string comparer tokens used by generated dictionary construction.
/// </summary>
class StringComparer {
public:
    static const StringComparer OrdinalIgnoreCase;
};

inline const StringComparer StringComparer::OrdinalIgnoreCase = StringComparer();
