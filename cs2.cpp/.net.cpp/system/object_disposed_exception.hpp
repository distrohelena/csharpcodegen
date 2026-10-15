#pragma once
#include "../runtime/native_exceptions.hpp"

/// <summary>Reports attempts to use an object after its owned resources were released.</summary>
class ObjectDisposedException : public InvalidOperationException {
public:
    /// <summary>Constructs a disposed-object failure without an object name.</summary>
    ObjectDisposedException() : InvalidOperationException("Cannot access a disposed object.") {}
    /// <summary>Identifies the disposed object using a managed string payload.</summary>
    explicit ObjectDisposedException(const HeCppString& objectName)
        : InvalidOperationException("Cannot access a disposed object: " + objectName) {}
    /// <summary>Identifies the disposed object using a native string literal.</summary>
    explicit ObjectDisposedException(const char* objectName)
        : ObjectDisposedException(HeCppString(objectName == nullptr ? "" : objectName)) {}
};
