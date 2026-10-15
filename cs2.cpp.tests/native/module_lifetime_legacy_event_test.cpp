#include "runtime/native_event.hpp"

/// <summary>Counts legacy event invocations without providing module-lifetime hooks.</summary>
static int LegacyCalls = 0;

/// <summary>Handles a legacy subscription with no module loader linked into the application.</summary>
static void OnLegacyEvent(int value) {
    LegacyCalls += value;
}

/// <summary>Proves monolithic event consumers still link and execute without a module backend.</summary>
int main() {
    Event event;
    event += &OnLegacyEvent;
    event.Invoke(3);
    event -= &OnLegacyEvent;
    event.Invoke(4);
    return LegacyCalls == 3 ? 0 : 1;
}
