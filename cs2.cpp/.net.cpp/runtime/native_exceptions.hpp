#pragma once

#include <stdexcept>
#include <string>

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
