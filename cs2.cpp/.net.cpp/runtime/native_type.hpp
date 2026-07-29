#pragma once

#include <string>
#include <type_traits>

/// <summary>
/// Represents a lightweight managed type token used by transpiled code on constrained native targets.
/// </summary>
class Type {
public:
    /// <summary>
    /// Initializes the token with the stable managed type name used for diagnostics and registration.
    /// </summary>
    /// <param name="name">Stable source-side type name associated with the token.</param>
    /// <param name="isEnum">Whether the represented managed type is one enum type.</param>
    constexpr explicit Type(const char* name, bool isEnum)
        : Name(name),
          IsEnum(isEnum) {
    }

    std::string get_Name() const {
        return Name == nullptr ? std::string() : std::string(Name);
    }

    bool get_IsEnum() const {
        return IsEnum;
    }

    /// <summary>
    /// Gets the stable managed type name represented by this token.
    /// </summary>
    const char* Name;

    /// <summary>
    /// Gets whether the represented managed type is one enum value type.
    /// </summary>
    bool IsEnum;
};

/// <summary>
/// Gets the canonical lightweight type token for one transpiled managed type.
/// </summary>
/// <typeparam name="T">Native type associated with the managed token.</typeparam>
/// <param name="name">Stable managed type name captured from the source syntax.</param>
/// <returns>Address-stable type token that can be compared by pointer identity.</returns>
template <typename T>
inline Type* he_cpp_type_of(const char* name) {
    static Type Token(name, std::is_enum_v<T>);
    return &Token;
}
