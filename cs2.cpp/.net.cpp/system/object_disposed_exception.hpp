#pragma once
#include "../runtime/native_exceptions.hpp"

/// <summary>Reports attempts to use an object after its owned resources were released.</summary>
class ObjectDisposedException : public InvalidOperationException {
    /// <summary>Retains the disposed object's name independently of the selected exception-message policy.</summary>
    HeCppString ObjectNameValue;

public:
    /// <summary>Constructs a disposed-object failure without an object name.</summary>
    ObjectDisposedException()
        : InvalidOperationException("Cannot access a disposed object."), ObjectNameValue("") {}

    /// <summary>Identifies the disposed object using a managed string payload.</summary>
    explicit ObjectDisposedException(const HeCppString& objectName)
        : InvalidOperationException("Cannot access a disposed object: " + objectName),
          ObjectNameValue(objectName.is_null() ? HeCppString("") : objectName) {}

    /// <summary>Identifies the disposed object using a native string literal.</summary>
    explicit ObjectDisposedException(const char* objectName)
        : ObjectDisposedException(HeCppString(objectName == nullptr ? "" : objectName)) {}

    /// <summary>Retains the managed object-name and message constructor contract, including unnamed objects and null-message fallback.</summary>
    ObjectDisposedException(const HeCppString& objectName, const HeCppString& message)
        : InvalidOperationException(BuildMessage(objectName, message)),
          ObjectNameValue(objectName.is_null() ? HeCppString("") : objectName) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        Message = "Cannot access a disposed object.";
#endif
    }

    /// <summary>Returns the disposed object's name, using an empty managed string when no name was supplied.</summary>
    const HeCppString& get_ObjectName() const {
        return ObjectNameValue;
    }

private:
    /// <summary>Formats the descriptive managed message and object-name suffix while honoring compact native messages.</summary>
    static HeCppString BuildMessage(const HeCppString& objectName, const HeCppString& message) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        (void)objectName;
        (void)message;
        return HeCppString("Cannot access a disposed object.");
#else
        HeCppString result = message.is_null()
            ? HeCppString("Cannot access a disposed object.")
            : message;
        if (!objectName.is_null() && !objectName.empty()) {
#if defined(_WIN32)
            result += "\r\n";
#else
            result += "\n";
#endif
            result += "Object name: '";
            result += objectName;
            result += "'.";
        }
        return result;
#endif
    }
};
