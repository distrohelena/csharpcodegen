#include "DeferredStaticGenericOwner.hpp"
#include "DeferredStaticResidentOwner.hpp"
/// <summary>Touches both the library-owned generic constant and the executable's shared constant.</summary>
extern "C" int ModuleTouch() {
    return DeferredStaticGenericOwner<int>::Get().Value + DeferredStaticResidentOwner::Get().Value;
}
