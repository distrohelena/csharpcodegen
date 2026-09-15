#include "runtime/native_event.hpp"
#include "system/action.hpp"
#include "system/func.hpp"
#include "system/diagnostics/stopwatch.hpp"
static int total = 0;
static std::uint64_t ticks = 0;
static void Add(int value) { total += value; }
namespace he_cpp_custom {
[[noreturn]] void Fail(const char*) { __builtin_trap(); }
std::uint64_t MonotonicMicroseconds() { return ticks; }
}
int main() {
    Event event;
    event += &Add;
    event.Invoke(3);
    event -= &Add;
    event.Invoke(8);
    Action<int> action([](int value) { total += value; });
    action(5);
    Func<int> result([]() { return total; });
    if (result() != 8) return 1;
#if !HE_CPP_USE_STD_CHRONO
    Stopwatch watch;
    watch.Start();
    ticks = 1250;
    if (watch.get_Elapsed().TotalMilliseconds != 1.25) return 2;
    watch.Stop();
    ticks = 9000;
    if (watch.get_Elapsed().TotalMilliseconds != 1.25) return 3;
#endif
    return 0;
}
