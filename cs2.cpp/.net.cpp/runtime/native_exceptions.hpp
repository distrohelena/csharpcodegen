#pragma once

#include "helcpp_config.hpp"

#include <exception>
#include <string>

#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES

class Exception : public std::exception {
protected:
    const char* Message;

public:
    Exception() noexcept
        : Message("Exception") {
    }

    explicit Exception(const char* message) noexcept
        : Message("Exception") {
        (void)message;
    }

    explicit Exception(const std::string& message) noexcept
        : Message("Exception") {
        (void)message;
    }

    const char* what() const noexcept override {
        return Message;
    }
};

class ArgumentException : public Exception {
public:
    ArgumentException() noexcept
        : Exception() {
        Message = "Invalid argument.";
    }

    explicit ArgumentException(const char* message) noexcept
        : Exception(message) {
        Message = "Invalid argument.";
    }

    explicit ArgumentException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Invalid argument.";
    }

    ArgumentException(const char* message, const char* parameterName) noexcept
        : Exception() {
        (void)message;
        (void)parameterName;
        Message = "Invalid argument.";
    }

    ArgumentException(const std::string& message, const std::string& parameterName) noexcept
        : Exception() {
        (void)message;
        (void)parameterName;
        Message = "Invalid argument.";
    }
};

class ArgumentNullException : public ArgumentException {
public:
    ArgumentNullException() noexcept
        : ArgumentException() {
        Message = "Value cannot be null.";
    }

    explicit ArgumentNullException(const char* parameterName) noexcept
        : ArgumentException() {
        (void)parameterName;
        Message = "Value cannot be null.";
    }

    explicit ArgumentNullException(const std::string& parameterName) noexcept
        : ArgumentException() {
        (void)parameterName;
        Message = "Value cannot be null.";
    }

    ArgumentNullException(const char* parameterName, const char* message) noexcept
        : ArgumentException() {
        (void)parameterName;
        (void)message;
        Message = "Value cannot be null.";
    }

    ArgumentNullException(const std::string& parameterName, const std::string& message) noexcept
        : ArgumentException() {
        (void)parameterName;
        (void)message;
        Message = "Value cannot be null.";
    }
};

class ArgumentOutOfRangeException : public ArgumentException {
public:
    ArgumentOutOfRangeException() noexcept
        : ArgumentException() {
        Message = "Specified argument was out of range.";
    }

    explicit ArgumentOutOfRangeException(const char* parameterName) noexcept
        : ArgumentException() {
        (void)parameterName;
        Message = "Specified argument was out of range.";
    }

    explicit ArgumentOutOfRangeException(const std::string& parameterName) noexcept
        : ArgumentException() {
        (void)parameterName;
        Message = "Specified argument was out of range.";
    }

    ArgumentOutOfRangeException(const char* parameterName, const char* message) noexcept
        : ArgumentException() {
        (void)parameterName;
        (void)message;
        Message = "Specified argument was out of range.";
    }

    ArgumentOutOfRangeException(const std::string& parameterName, const std::string& message) noexcept
        : ArgumentException() {
        (void)parameterName;
        (void)message;
        Message = "Specified argument was out of range.";
    }
};

class InvalidOperationException : public Exception {
public:
    InvalidOperationException() noexcept
        : Exception() {
        Message = "Operation is not valid due to the current state of the object.";
    }

    explicit InvalidOperationException(const char* message) noexcept
        : Exception(message) {
        Message = "Operation is not valid due to the current state of the object.";
    }

    explicit InvalidOperationException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Operation is not valid due to the current state of the object.";
    }
};

class KeyNotFoundException : public Exception {
public:
    KeyNotFoundException() noexcept
        : Exception() {
        Message = "The specified key was not found.";
    }

    explicit KeyNotFoundException(const char* message) noexcept
        : Exception(message) {
        Message = "The specified key was not found.";
    }

    explicit KeyNotFoundException(const std::string& message) noexcept
        : Exception(message) {
        Message = "The specified key was not found.";
    }
};

class DivideByZeroException : public Exception {
public:
    DivideByZeroException() noexcept
        : Exception() {
        Message = "Attempted to divide by zero.";
    }

    explicit DivideByZeroException(const char* message) noexcept
        : Exception(message) {
        Message = "Attempted to divide by zero.";
    }

    explicit DivideByZeroException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Attempted to divide by zero.";
    }
};

/// <summary>
/// Represents a managed checked-arithmetic result that exceeds its destination type range.
/// </summary>
class OverflowException : public Exception {
public:
    /// <summary>
    /// Creates a compact overflow exception with the canonical runtime message.
    /// </summary>
    OverflowException() noexcept
        : Exception() {
        Message = "Arithmetic operation resulted in an overflow.";
    }

    /// <summary>
    /// Creates a compact overflow exception while discarding the supplied message payload.
    /// </summary>
    explicit OverflowException(const char* message) noexcept
        : Exception(message) {
        Message = "Arithmetic operation resulted in an overflow.";
    }

    /// <summary>
    /// Creates a compact overflow exception while discarding the supplied managed string payload.
    /// </summary>
    explicit OverflowException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Arithmetic operation resulted in an overflow.";
    }
};

class EndOfStreamException : public Exception {
public:
    EndOfStreamException() noexcept
        : Exception() {
        Message = "Unable to read beyond the end of the stream.";
    }

    explicit EndOfStreamException(const char* message) noexcept
        : Exception(message) {
        Message = "Unable to read beyond the end of the stream.";
    }

    explicit EndOfStreamException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Unable to read beyond the end of the stream.";
    }
};

class FileNotFoundException : public Exception {
public:
    FileNotFoundException() noexcept
        : Exception() {
        Message = "Unable to find the specified file.";
    }

    explicit FileNotFoundException(const char* message) noexcept
        : Exception(message) {
        Message = "Unable to find the specified file.";
    }

    explicit FileNotFoundException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Unable to find the specified file.";
    }

    FileNotFoundException(const char* message, const char* fileName) noexcept
        : Exception() {
        (void)message;
        (void)fileName;
        Message = "Unable to find the specified file.";
    }

    FileNotFoundException(const std::string& message, const std::string& fileName) noexcept
        : Exception() {
        (void)message;
        (void)fileName;
        Message = "Unable to find the specified file.";
    }
};

class DirectoryNotFoundException : public Exception {
public:
    DirectoryNotFoundException() noexcept
        : Exception() {
        Message = "Unable to find the specified directory.";
    }

    explicit DirectoryNotFoundException(const char* message) noexcept
        : Exception(message) {
        Message = "Unable to find the specified directory.";
    }

    explicit DirectoryNotFoundException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Unable to find the specified directory.";
    }
};

class NotSupportedException : public Exception {
public:
    NotSupportedException() noexcept
        : Exception() {
        Message = "Specified method is not supported.";
    }

    explicit NotSupportedException(const char* message) noexcept
        : Exception(message) {
        Message = "Specified method is not supported.";
    }

    explicit NotSupportedException(const std::string& message) noexcept
        : Exception(message) {
        Message = "Specified method is not supported.";
    }
};

#else

#include <stdexcept>

class Exception : public std::runtime_error {
public:
    Exception()
        : std::runtime_error("Exception") {
    }

    explicit Exception(const char* message)
        : std::runtime_error(message == nullptr ? "Exception" : message) {
    }

    explicit Exception(const std::string& message)
        : std::runtime_error(message) {
    }
};

class ArgumentException : public Exception {
public:
    ArgumentException()
        : Exception("Invalid argument.") {
    }

    explicit ArgumentException(const char* message)
        : Exception(message == nullptr ? "Invalid argument." : message) {
    }

    explicit ArgumentException(const std::string& message)
        : Exception(message) {
    }

    ArgumentException(const char* message, const char* parameterName)
        : ArgumentException(
            std::string(message == nullptr ? "Invalid argument." : message) +
            " Parameter name: " +
            (parameterName == nullptr ? "" : parameterName)) {
    }

    ArgumentException(const std::string& message, const std::string& parameterName)
        : Exception(message + " Parameter name: " + parameterName) {
    }
};

class ArgumentNullException : public ArgumentException {
public:
    ArgumentNullException()
        : ArgumentException("Value cannot be null.") {
    }

    explicit ArgumentNullException(const char* parameterName)
        : ArgumentException(
            std::string("Value cannot be null. Parameter name: ") +
            (parameterName == nullptr ? "" : parameterName)) {
    }

    explicit ArgumentNullException(const std::string& parameterName)
        : ArgumentException("Value cannot be null. Parameter name: " + parameterName) {
    }

    ArgumentNullException(const char* parameterName, const char* message)
        : ArgumentException(
            message == nullptr ? "Value cannot be null." : message,
            parameterName == nullptr ? "" : parameterName) {
    }

    ArgumentNullException(const std::string& parameterName, const std::string& message)
        : ArgumentException(message, parameterName) {
    }
};

class ArgumentOutOfRangeException : public ArgumentException {
public:
    ArgumentOutOfRangeException()
        : ArgumentException("Specified argument was out of range.") {
    }

    explicit ArgumentOutOfRangeException(const char* parameterName)
        : ArgumentException(
            std::string("Specified argument was out of range. Parameter name: ") +
            (parameterName == nullptr ? "" : parameterName)) {
    }

    explicit ArgumentOutOfRangeException(const std::string& parameterName)
        : ArgumentException("Specified argument was out of range. Parameter name: " + parameterName) {
    }

    ArgumentOutOfRangeException(const char* parameterName, const char* message)
        : ArgumentException(
            std::string(message == nullptr ? "Specified argument was out of range." : message) +
            " Parameter name: " +
            (parameterName == nullptr ? "" : parameterName)) {
    }

    ArgumentOutOfRangeException(const std::string& parameterName, const std::string& message)
        : ArgumentException(message + " Parameter name: " + parameterName) {
    }
};

class InvalidOperationException : public Exception {
public:
    InvalidOperationException()
        : Exception("Operation is not valid due to the current state of the object.") {
    }

    explicit InvalidOperationException(const char* message)
        : Exception(message == nullptr ? "Operation is not valid due to the current state of the object." : message) {
    }

    explicit InvalidOperationException(const std::string& message)
        : Exception(message) {
    }
};

class KeyNotFoundException : public Exception {
public:
    KeyNotFoundException()
        : Exception("The specified key was not found.") {
    }

    explicit KeyNotFoundException(const char* message)
        : Exception(message == nullptr ? "The specified key was not found." : message) {
    }

    explicit KeyNotFoundException(const std::string& message)
        : Exception(message) {
    }
};

class DivideByZeroException : public Exception {
public:
    DivideByZeroException()
        : Exception("Attempted to divide by zero.") {
    }

    explicit DivideByZeroException(const char* message)
        : Exception(message == nullptr ? "Attempted to divide by zero." : message) {
    }

    explicit DivideByZeroException(const std::string& message)
        : Exception(message) {
    }
};

/// <summary>
/// Represents a managed checked-arithmetic result that exceeds its destination type range.
/// </summary>
class OverflowException : public Exception {
public:
    /// <summary>
    /// Creates an overflow exception with the canonical runtime message.
    /// </summary>
    OverflowException()
        : Exception("Arithmetic operation resulted in an overflow.") {
    }

    /// <summary>
    /// Creates an overflow exception with an optional caller-provided message.
    /// </summary>
    explicit OverflowException(const char* message)
        : Exception(message == nullptr ? "Arithmetic operation resulted in an overflow." : message) {
    }

    /// <summary>
    /// Creates an overflow exception with a caller-provided managed string message.
    /// </summary>
    explicit OverflowException(const std::string& message)
        : Exception(message) {
    }
};

class EndOfStreamException : public Exception {
public:
    EndOfStreamException()
        : Exception("Unable to read beyond the end of the stream.") {
    }

    explicit EndOfStreamException(const char* message)
        : Exception(message == nullptr ? "Unable to read beyond the end of the stream." : message) {
    }

    explicit EndOfStreamException(const std::string& message)
        : Exception(message) {
    }
};

class FileNotFoundException : public Exception {
public:
    FileNotFoundException()
        : Exception("Unable to find the specified file.") {
    }

    explicit FileNotFoundException(const char* message)
        : Exception(message == nullptr ? "Unable to find the specified file." : message) {
    }

    explicit FileNotFoundException(const std::string& message)
        : Exception(message) {
    }

    FileNotFoundException(const char* message, const char* fileName)
        : Exception(
            std::string(message == nullptr ? "Unable to find the specified file." : message) +
            " File name: " +
            (fileName == nullptr ? "" : fileName)) {
    }

    FileNotFoundException(const std::string& message, const std::string& fileName)
        : Exception(message + " File name: " + fileName) {
    }
};

class DirectoryNotFoundException : public Exception {
public:
    DirectoryNotFoundException()
        : Exception("Unable to find the specified directory.") {
    }

    explicit DirectoryNotFoundException(const char* message)
        : Exception(message == nullptr ? "Unable to find the specified directory." : message) {
    }

    explicit DirectoryNotFoundException(const std::string& message)
        : Exception(message) {
    }
};

class NotSupportedException : public Exception {
public:
    NotSupportedException()
        : Exception("Specified method is not supported.") {
    }

    explicit NotSupportedException(const char* message)
        : Exception(message == nullptr ? "Specified method is not supported." : message) {
    }

    explicit NotSupportedException(const std::string& message)
        : Exception(message) {
    }
};

#endif
