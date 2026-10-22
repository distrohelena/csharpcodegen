#pragma once

#include "../../runtime/array.hpp"
#include "../../runtime/native_string.hpp"
#include "../../runtime/native_exceptions.hpp"

/// <summary>
/// Represents the minimal managed Encoding surface required by transpiled UTF-8 oriented code paths.
/// </summary>
class Encoding {
public:
    /// <summary>
    /// Gets the shared UTF-8 encoding marker used by transpiled readers and writers.
    /// </summary>
    static const Encoding UTF8;

    /// <summary>
    /// Encodes a UTF-8 string into a managed byte-array wrapper.
    /// </summary>
    static Array<uint8_t>* GetBytes(const Encoding&, const HeCppString& value) {
        if (value.is_null()) {
            he_cpp_raise(ArgumentNullException("value"));
        }
        const auto units = he_cpp_string_chars(value);
        const HeCppRawString encoded = HeCppUnicode::FromUtf16<HeCppRawString>(units.data(), units.size(), true);
        Array<uint8_t>* bytes = new Array<uint8_t>(static_cast<int32_t>(encoded.size()));
        for (int32_t index = 0; index < bytes->Length; ++index) { (*bytes)[index] = static_cast<uint8_t>(encoded[static_cast<size_t>(index)]); }

        return bytes;
    }

    /// <summary>
    /// Decodes a managed byte-array wrapper as a UTF-8 string.
    /// </summary>
    static HeCppString GetString(const Encoding&, Array<uint8_t>* value) {
        if (value == nullptr) {
            he_cpp_raise(ArgumentNullException("value"));
        }
        if (value->Length == 0) {
            return HeCppString("");
        }

        HeCppRawString decoded; size_t offset = 0;
        while (offset < static_cast<size_t>(value->Length)) {
            HeCppUnicode::Append(decoded, HeCppUnicode::Decode(reinterpret_cast<const char*>(value->Data), static_cast<size_t>(value->Length), offset, false));
        }
        return HeCppString(decoded);
    }
};

inline const Encoding Encoding::UTF8 = Encoding();
