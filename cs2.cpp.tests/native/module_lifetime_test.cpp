#include "runtime/native_module_pinned_callable.hpp"
#include "runtime/native_runtime.hpp"
#include "runtime/native_event.hpp"
#include <utility>

#if defined(HE_CPP_PLATFORM_PS1)
/// <summary>Provides a freestanding assertion without requiring a hosted assert library.</summary>
static void ModulePinCheck(bool condition) {
    if (!condition) {
        __builtin_trap();
    }
}
#define assert(condition) ModulePinCheck(condition)
#else
#include <cassert>
#endif

/// <summary>Compares IDs without adding a hosted string library to the SDK proof.</summary>
static bool IsFixtureModule(const char* moduleId) {
    const char* expected = "scene.fixture";
    while (*moduleId != '\0' && *moduleId == *expected) {
        ++moduleId;
        ++expected;
    }
    return *moduleId == *expected;
}

/// <summary>Tracks live code references independently from callable invocation.</summary>
static int LivePins = 0;

/// <summary>Models the resident loader's retain hook for one known module.</summary>
void HeCppRetainCodeModule(const char* moduleId) {
    assert(moduleId != nullptr);
    if (moduleId[0] == '\0') {
        return;
    }
    assert(IsFixtureModule(moduleId));
    ++LivePins;
}

/// <summary>Models release without clearing code from inside the callable's destructor.</summary>
void HeCppReleaseCodeModule(const char* moduleId) {
    assert(moduleId != nullptr);
    if (moduleId[0] == '\0') {
        return;
    }
    assert(IsFixtureModule(moduleId) && LivePins > 0);
    --LivePins;
}

/// <summary>Ensures closure destruction executes before the final module pin is released.</summary>
class FixtureCallable {
public:
    /// <summary>Verifies its module is mapped when the callback runs.</summary>
    int& operator()(int& value) const {
        assert(LivePins > 0);
        ++value;
        return value;
    }

    /// <summary>Detects releasing the code pin before destroying callable storage.</summary>
    ~FixtureCallable() {
        assert(LivePins > 0);
    }
};

/// <summary>Tracks invocation through the event's removable function-pointer token.</summary>
static int EventCalls = 0;

/// <summary>Verifies a free-function event callback runs with a live module pin.</summary>
static void OnFixtureEvent(int value) {
    assert(LivePins > 0);
    EventCalls += value;
}

/// <summary>Exercises bound receiver identity independently of the subscriber's code pin.</summary>
class FixtureReceiver {
public:
    /// <summary>Counts callback arguments delivered to this particular receiver.</summary>
    int Received = 0;

    /// <summary>Verifies pinned member callbacks receive their arguments.</summary>
    void OnEvent(int value) {
        assert(LivePins > 0);
        Received += value;
    }
};

/// <summary>Exercises escaping delegates, independent copies, moves and destruction ordering.</summary>
int main() {
    {
        HeCppCodeModulePin resident("");
        assert(LivePins == 0);
        HeCppCodeModulePin pin("scene.fixture");
        HeCppCodeModulePin copy(pin);
        assert(LivePins == 2);
        HeCppCodeModulePin moved(std::move(copy));
        assert(LivePins == 2);
        copy = pin;
        assert(LivePins == 3);
        moved = std::move(copy);
        assert(LivePins == 2);
        pin = pin;
        moved = std::move(moved);
        assert(LivePins == 2);
    }
    assert(LivePins == 0);
    HeCppFunction<int&(int&)> escaped;
    {
        auto owner = HeCppPinCodeModuleCallable("scene.fixture", FixtureCallable());
        assert(LivePins == 1);
        escaped = owner;
        assert(LivePins == 2);
        const auto copied = owner;
        int value = 2;
        int& returned = copied(value);
        assert(&returned == &value && value == 3);
        assert(LivePins == 3);
    }
    assert(LivePins == 1);
    int value = 10;
    assert(&escaped(value) == &value && value == 11);
    auto second = escaped;
    assert(LivePins == 2);
    escaped = nullptr;
    assert(LivePins == 1);
    second = nullptr;
    assert(LivePins == 0);
    {
        auto mutableCallback = HeCppPinCodeModuleCallable(
            "scene.fixture", [calls = 0]() mutable { return ++calls; });
        assert(mutableCallback() == 1 && mutableCallback() == 2);
        HeCppFunction<int()> deferredMutable = mutableCallback;
        assert(deferredMutable() == 3 && deferredMutable() == 4);
        auto noResult = HeCppPinCodeModuleCallable(
            "scene.fixture", [&value]() { value = 42; });
        noResult();
        assert(value == 42 && LivePins == 3);
    }
    assert(LivePins == 0);
    {
        Event event;
        FixtureReceiver receiver;
        event += Event::PinFunction("scene.fixture", &OnFixtureEvent);
        event += Event::BindPinned("scene.fixture", &receiver, &FixtureReceiver::OnEvent);
        assert(LivePins == 2);
        event.Invoke(3);
        assert(EventCalls == 3 && receiver.Received == 3);
        event -= Event::BindPinned("scene.fixture", &receiver, &FixtureReceiver::OnEvent);
        assert(LivePins == 1);
        event.Invoke(4);
        assert(EventCalls == 7 && receiver.Received == 3);
        event -= &OnFixtureEvent;
        assert(LivePins == 0);
        event += Event::PinFunction("scene.fixture", &OnFixtureEvent);
        event -= Event::PinFunction("scene.fixture", &OnFixtureEvent);
        assert(LivePins == 0);
        event += Event::BindPinned("scene.fixture", &receiver, &FixtureReceiver::OnEvent);
        event -= Event::Bind(&receiver, &FixtureReceiver::OnEvent);
        assert(LivePins == 0);
        event += Event::PinFunction("scene.fixture", &OnFixtureEvent);
        event += Event::BindPinned("scene.fixture", &receiver, &FixtureReceiver::OnEvent);
        assert(LivePins == 2);
    }
    assert(LivePins == 0);
    return 0;
}
