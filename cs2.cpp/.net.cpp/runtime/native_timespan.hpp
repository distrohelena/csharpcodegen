#pragma once

/// <summary>
/// Represents a lightweight managed-style duration expressed in milliseconds.
/// </summary>
class TimeSpan {
public:
    double TotalMilliseconds;

    TimeSpan()
        : TotalMilliseconds(0.0) {
    }

    explicit TimeSpan(double totalMilliseconds)
        : TotalMilliseconds(totalMilliseconds) {
    }

    double get_TotalMilliseconds() const {
        return TotalMilliseconds;
    }
};
