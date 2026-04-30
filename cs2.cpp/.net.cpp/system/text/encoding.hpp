#pragma once

/// <summary>
/// Represents the minimal managed Encoding surface required by transpiled UTF-8 oriented code paths.
/// </summary>
class Encoding {
public:
    /// <summary>
    /// Gets the shared UTF-8 encoding marker used by transpiled readers and writers.
    /// </summary>
    static const Encoding UTF8;
};

inline const Encoding Encoding::UTF8 = Encoding();
