#include "DeferredStaticResidentOwner.hpp"
#include <cstdio>
#include <dlfcn.h>
extern "C" {
    int ModuleDestructions = 0;
    int ResidentDestructions = 0;
}
/// <summary>Loads and retires the same library twice; its own value must be freed while the shared resident value must survive.</summary>
int main(int argc, char** argv) {
    if (argc != 2 || DeferredStaticResidentOwner::Get().Value != 37) return 1;
    for (int generation = 1; generation <= 2; ++generation) {
        void* module = dlopen(argv[1], RTLD_NOW | RTLD_GLOBAL);
        if (!module) { std::puts(dlerror()); return 2; }
        auto touch = reinterpret_cast<int(*)()>(dlsym(module, "ModuleTouch"));
        if (!touch || touch() != 74) return 3;
        if (dlclose(module)) return 4;
        std::printf("generation=%d module_destructions=%d resident_destructions=%d\n",
            generation, ModuleDestructions, ResidentDestructions);
        if (ModuleDestructions != generation || ResidentDestructions != 0) return 5;
        if (DeferredStaticResidentOwner::Get().Value != 37) return 6;
    }
    return 0;
}
