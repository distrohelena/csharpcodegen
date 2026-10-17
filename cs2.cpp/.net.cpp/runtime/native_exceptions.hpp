#pragma once

#include "native_runtime.hpp"

#ifndef HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
#define HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES 0
#endif

#if HE_CPP_USE_EXCEPTIONS
#include <exception>
#if HE_CPP_USE_STD_STRING && !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
#include <stdexcept>
#endif
#endif

namespace he_cpp_exception_detail {

/// <summary>
/// Selects either a canonical compact message or the caller's full message according to the runtime configuration.
/// </summary>
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
inline const char* SelectMessage(const char* message, const char* fallback) {
#else
inline HeCppString SelectMessage(const char* message, const char* fallback) {
#endif
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    (void)message;
    return fallback;
#else
    return HeCppString(message == nullptr ? fallback : message);
#endif
}

/// <summary>
/// Selects either a canonical compact message or the caller's full managed string message.
/// </summary>
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
inline const char* SelectMessage(const HeCppString& message, const char* fallback) {
#else
inline HeCppString SelectMessage(const HeCppString& message, const char* fallback) {
#endif
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    (void)message;
    return fallback;
#else
    (void)fallback;
    return message;
#endif
}

/// <summary>
/// Builds a full exception message with an optional parameter name while retaining compact-message behavior.
/// </summary>
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
inline const char* MessageWithParameter(const char* message, const char* parameterName, const char* fallback) {
#else
inline HeCppString MessageWithParameter(const char* message, const char* parameterName, const char* fallback) {
#endif
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    (void)message;
    (void)parameterName;
    return fallback;
#else
    HeCppString result = SelectMessage(message, fallback);
    result += " Parameter name: ";
    if (parameterName != nullptr) {
        result += parameterName;
    }
    return result;
#endif
}

/// <summary>
/// Builds a full exception message from managed string values while retaining compact-message behavior.
/// </summary>
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
inline const char* MessageWithParameter(const HeCppString& message, const HeCppString& parameterName, const char* fallback) {
#else
inline HeCppString MessageWithParameter(const HeCppString& message, const HeCppString& parameterName, const char* fallback) {
#endif
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    (void)message;
    (void)parameterName;
    return fallback;
#else
    (void)fallback;
    HeCppString result = message;
    result += " Parameter name: ";
    result += parameterName;
    return result;
#endif
}

/// <summary>
/// Builds a full missing-file message with the historical file-name label while retaining compact-message behavior.
/// </summary>
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
inline const char* MessageWithFileName(const char* message, const char* fileName, const char* fallback) {
    (void)message;
    (void)fileName;
    return fallback;
}

inline const char* MessageWithFileName(const HeCppString& message, const HeCppString& fileName, const char* fallback) {
    (void)message;
    (void)fileName;
    return fallback;
}
#else
inline HeCppString MessageWithFileName(const char* message, const char* fileName, const char* fallback) {
    HeCppString result = SelectMessage(message, fallback);
    result += " File name: ";
    if (fileName != nullptr) {
        result += fileName;
    }
    return result;
}

inline HeCppString MessageWithFileName(const HeCppString& message, const HeCppString& fileName, const char* fallback) {
    (void)fallback;
    HeCppString result = message;
    result += " File name: ";
    result += fileName;
    return result;
}
#endif

}

/// <summary>
/// Represents the base managed exception payload used by generated runtime failures.
/// </summary>
#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING && !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
class Exception : public std::runtime_error {
#elif HE_CPP_USE_EXCEPTIONS
class Exception : public std::exception {
#else
class Exception {
#endif
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES || !(HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING)
protected:
#endif
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    /// <summary>
    /// Stores the canonical compact literal without allocating a managed string.
    /// </summary>
    const char* Message;
#elif !(HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING)
    /// <summary>
    /// Stores the selected managed failure message.
    /// </summary>
    HeCppString Message;
#endif

public:
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    /// <summary>
    /// Initializes an exception from one managed string message.
    /// </summary>
    explicit Exception(const HeCppString& message)
        : Message("Exception") {
        (void)message;
    }

    /// <summary>
    /// Initializes an exception from one literal message.
    /// </summary>
    explicit Exception(const char* message)
        : Message("Exception") {
        (void)message;
    }
#elif HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING

    /// <summary>
    /// Initializes the standard hosted exception with one managed string message.
    /// </summary>
    explicit Exception(const HeCppString& message)
        : std::runtime_error(message.c_str()) {
    }

    /// <summary>
    /// Initializes the standard hosted exception with one literal message.
    /// </summary>
    explicit Exception(const char* message)
        : std::runtime_error(message == nullptr ? "Exception" : message) {
    }
#else

    /// <summary>
    /// Initializes a non-hosted exception value from one managed string message.
    /// </summary>
    explicit Exception(const HeCppString& message)
        : Message(message) {
    }

    /// <summary>
    /// Initializes a non-hosted exception value from one literal message.
    /// </summary>
    explicit Exception(const char* message)
        : Message(message == nullptr ? HeCppString("Exception") : HeCppString(message)) {
    }
#endif

public:
    /// <summary>
    /// Initializes an exception with the canonical base message.
    /// </summary>
    Exception()
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        : Message("Exception") {
#elif HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
        : std::runtime_error("Exception") {
#else
        : Message("Exception") {
 #endif
    }

    /// <summary>
    /// Returns the failure message as a stable C string for standard exception consumers and custom fatal hooks.
    /// </summary>
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    const char* what() const noexcept
#if HE_CPP_USE_EXCEPTIONS
        override
#endif
    {
        return Message;
    }
#elif HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
#else
    const char* what() const noexcept
#if HE_CPP_USE_EXCEPTIONS
        override
#endif
    {
        return Message.c_str();
    }
#endif
};

/// <summary>
/// Represents multiple managed failures using the selected native exception message policy.
/// </summary>
class AggregateException : public Exception {
public:
    /// <summary>
    /// Initializes an aggregate failure with the standard managed aggregate message.
    /// </summary>
    AggregateException()
        : AggregateException("One or more errors occurred.") {
    }

    /// <summary>
    /// Initializes an aggregate failure from a literal message while respecting the compact-message policy.
    /// </summary>
    explicit AggregateException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "One or more errors occurred.")) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        Message = "One or more errors occurred.";
#endif
    }

    /// <summary>
    /// Initializes an aggregate failure from a managed string while respecting the compact-message policy.
    /// </summary>
    explicit AggregateException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "One or more errors occurred.")) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        Message = "One or more errors occurred.";
#endif
    }

    /// <summary>
    /// Accepts two native failures and a literal message without allocating an inner-exception collection.
    /// </summary>
    AggregateException(const char* message, Exception* firstException, Exception* secondException)
        : AggregateException(message) {
        (void)firstException;
        (void)secondException;
    }

    /// <summary>
    /// Accepts two native failures and a managed string without allocating an inner-exception collection.
    /// </summary>
    AggregateException(const HeCppString& message, Exception* firstException, Exception* secondException)
        : AggregateException(message) {
        (void)firstException;
        (void)secondException;
    }
};

/// <summary>
/// Represents an invalid argument supplied to a generated managed operation.
/// </summary>
class ArgumentException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    ArgumentException()
        : Exception("Invalid argument.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit ArgumentException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Invalid argument.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit ArgumentException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Invalid argument.")) {
    }

    /// <summary>
    /// Initializes the exception from literal message and parameter name values.
    /// </summary>
    ArgumentException(const char* message, const char* parameterName)
        : Exception(he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Invalid argument.")) {
    }

    /// <summary>
    /// Initializes the exception from managed string message and parameter name values.
    /// </summary>
    ArgumentException(const HeCppString& message, const HeCppString& parameterName)
        : Exception(he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Invalid argument.")) {
    }
};

/// <summary>
/// Represents an argument that was unexpectedly null.
/// </summary>
class ArgumentNullException : public ArgumentException {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    ArgumentNullException()
        : ArgumentException("Value cannot be null.") {
    }

    /// <summary>
    /// Initializes the exception with a literal parameter name.
    /// </summary>
    explicit ArgumentNullException(const char* parameterName)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter("Value cannot be null.", parameterName, "Value cannot be null.")) {
    }

    /// <summary>
    /// Initializes the exception with a managed parameter name.
    /// </summary>
    explicit ArgumentNullException(const HeCppString& parameterName)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter(HeCppString("Value cannot be null."), parameterName, "Value cannot be null.")) {
    }

    /// <summary>
    /// Initializes the exception with literal parameter name and message values.
    /// </summary>
    ArgumentNullException(const char* parameterName, const char* message)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Value cannot be null.")) {
    }

    /// <summary>
    /// Initializes the exception with managed parameter name and message values.
    /// </summary>
    ArgumentNullException(const HeCppString& parameterName, const HeCppString& message)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Value cannot be null.")) {
    }
};

/// <summary>
/// Represents an argument whose value falls outside the operation's accepted range.
/// </summary>
class ArgumentOutOfRangeException : public ArgumentException {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    ArgumentOutOfRangeException()
        : ArgumentException("Specified argument was out of range.") {
    }

    /// <summary>
    /// Initializes the exception with a literal parameter name.
    /// </summary>
    explicit ArgumentOutOfRangeException(const char* parameterName)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter("Specified argument was out of range.", parameterName, "Specified argument was out of range.")) {
    }

    /// <summary>
    /// Initializes the exception with a managed parameter name.
    /// </summary>
    explicit ArgumentOutOfRangeException(const HeCppString& parameterName)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter(HeCppString("Specified argument was out of range."), parameterName, "Specified argument was out of range.")) {
    }

    /// <summary>
    /// Initializes the exception with literal parameter name and message values.
    /// </summary>
    ArgumentOutOfRangeException(const char* parameterName, const char* message)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Specified argument was out of range.")) {
    }

    /// <summary>
    /// Initializes the exception with managed parameter name and message values.
    /// </summary>
    ArgumentOutOfRangeException(const HeCppString& parameterName, const HeCppString& message)
        : ArgumentException(he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Specified argument was out of range.")) {
    }
    /// <summary>
    /// Retains the verified scalar or enum actual value as a diagnostic without pretending that the runtime supports general managed boxing.
    /// </summary>
    HeCppString ActualValueDiagnostic;

    /// <summary>
    /// Preserves a formatted actual value and the parameter/message failure contract supplied by generated scalar or enum construction.
    /// </summary>
    ArgumentOutOfRangeException(const HeCppString& parameterName, const HeCppString& actualValueDiagnostic, const HeCppString& message)
        : ArgumentException(BuildActualValueMessage(parameterName, actualValueDiagnostic, message)),
          ActualValueDiagnostic(actualValueDiagnostic) {
    }

    /// <summary>
    /// Applies the existing compact-message policy while retaining the actual-value diagnostic independently.
    /// </summary>
    static HeCppString BuildActualValueMessage(const HeCppString& parameterName, const HeCppString& actualValueDiagnostic, const HeCppString& message) {
        HeCppString result = he_cpp_exception_detail::MessageWithParameter(message, parameterName, "Specified argument was out of range.");
#if !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        result += "\nActual value was ";
        result += actualValueDiagnostic;
        result += ".";
#else
        (void)actualValueDiagnostic;
#endif
        return result;
    }
};

/// <summary>
/// Represents an operation that cannot proceed in the current object state.
/// </summary>
class InvalidOperationException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    InvalidOperationException()
        : Exception("Operation is not valid due to the current state of the object.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit InvalidOperationException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Operation is not valid due to the current state of the object.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit InvalidOperationException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Operation is not valid due to the current state of the object.")) {
    }
};

/// <summary>
/// Represents a lookup that did not find its requested key.
/// </summary>
class KeyNotFoundException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    KeyNotFoundException()
        : Exception("The specified key was not found.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit KeyNotFoundException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "The specified key was not found.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit KeyNotFoundException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "The specified key was not found.")) {
    }
};

/// <summary>
/// Represents an attempted division by zero.
/// </summary>
class DivideByZeroException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    DivideByZeroException()
        : Exception("Attempted to divide by zero.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit DivideByZeroException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Attempted to divide by zero.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit DivideByZeroException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Attempted to divide by zero.")) {
    }
};

/// <summary>Represents an undefined arithmetic operation, including the sign of NaN.</summary>
class ArithmeticException : public Exception {
public:
    /// <summary>Initializes the canonical arithmetic failure message.</summary>
    ArithmeticException() : Exception("Arithmetic operation resulted in an exception.") {}
    /// <summary>Preserves a caller message when full diagnostics are enabled.</summary>
    explicit ArithmeticException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Arithmetic operation resulted in an exception.")) {}
    /// <summary>Preserves a managed caller message when full diagnostics are enabled.</summary>
    explicit ArithmeticException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Arithmetic operation resulted in an exception.")) {}
};
/// <summary>
/// Represents checked arithmetic that exceeds the destination type range.
/// </summary>
class OverflowException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    OverflowException()
        : Exception("Arithmetic operation resulted in an overflow.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit OverflowException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Arithmetic operation resulted in an overflow.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit OverflowException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Arithmetic operation resulted in an overflow.")) {
    }
};

/// <summary>
/// Represents an attempt to read beyond available stream data.
/// </summary>
class EndOfStreamException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    EndOfStreamException()
        : Exception("Unable to read beyond the end of the stream.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit EndOfStreamException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Unable to read beyond the end of the stream.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit EndOfStreamException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Unable to read beyond the end of the stream.")) {
    }
};

/// <summary>
/// Represents a missing file requested by a generated operation.
/// </summary>
class FileNotFoundException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    FileNotFoundException()
        : Exception("Unable to find the specified file.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit FileNotFoundException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Unable to find the specified file.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit FileNotFoundException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Unable to find the specified file.")) {
    }

    /// <summary>
    /// Initializes the exception from literal message and file name values.
    /// </summary>
    FileNotFoundException(const char* message, const char* fileName)
        : Exception(he_cpp_exception_detail::MessageWithFileName(message, fileName, "Unable to find the specified file.")) {
    }

    /// <summary>
    /// Initializes the exception from managed string message and file name values.
    /// </summary>
    FileNotFoundException(const HeCppString& message, const HeCppString& fileName)
        : Exception(he_cpp_exception_detail::MessageWithFileName(message, fileName, "Unable to find the specified file.")) {
    }
};

/// <summary>
/// Represents a missing directory requested by a generated operation.
/// </summary>
class DirectoryNotFoundException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    DirectoryNotFoundException()
        : Exception("Unable to find the specified directory.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit DirectoryNotFoundException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Unable to find the specified directory.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit DirectoryNotFoundException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Unable to find the specified directory.")) {
    }
};

/// <summary>
/// Represents an operation unsupported by the selected runtime capabilities.
/// </summary>
class NotSupportedException : public Exception {
public:
    /// <summary>
    /// Initializes the exception with its canonical message.
    /// </summary>
    NotSupportedException()
        : Exception("Specified method is not supported.") {
    }

    /// <summary>
    /// Initializes the exception from a literal message.
    /// </summary>
    explicit NotSupportedException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Specified method is not supported.")) {
    }

    /// <summary>
    /// Initializes the exception from a managed string message.
    /// </summary>
    explicit NotSupportedException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Specified method is not supported.")) {
    }
};

/// <summary>Represents an instance operation attempted through a null managed reference.</summary>
class NullReferenceException : public Exception {
public:
    /// <summary>Initializes the canonical managed null-reference failure.</summary>
    NullReferenceException()
        : Exception("Object reference not set to an instance of an object.") {
    }

    /// <summary>Initializes the failure with the supplied literal message.</summary>
    explicit NullReferenceException(const char* message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Object reference not set to an instance of an object.")) {
    }

    /// <summary>Initializes the failure with the supplied managed message.</summary>
    explicit NullReferenceException(const HeCppString& message)
        : Exception(he_cpp_exception_detail::SelectMessage(message, "Object reference not set to an instance of an object.")) {
    }
};

/// <summary>Validates a managed string receiver while retaining its value and storage lifetime.</summary>
inline const HeCppString& he_cpp_require_string_value(const HeCppString& value) {
    if (value.is_null()) {
        he_cpp_raise(NullReferenceException());
    }
    return value;
}

#include "../system/object_disposed_exception.hpp"
