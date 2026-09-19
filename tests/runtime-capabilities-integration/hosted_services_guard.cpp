// Must fail to compile under the freestanding config: threading is a hosted service.
#include "system/threading/interlocked.hpp"
int main() { return 0; }
