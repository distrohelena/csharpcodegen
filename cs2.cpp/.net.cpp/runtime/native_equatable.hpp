#pragma once

/// <summary>
/// Represents the managed equatable contract expected by transpiled engine types.
/// </summary>
class IEquatable {
public:
    /// <summary>
    /// Releases the interface vtable through the expected polymorphic base destructor.
    /// </summary>
    virtual ~IEquatable() = default;
};
