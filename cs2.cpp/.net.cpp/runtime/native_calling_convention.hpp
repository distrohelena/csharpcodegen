#ifndef HE_CPP_RUNTIME_NATIVE_CALLING_CONVENTION_HPP
#define HE_CPP_RUNTIME_NATIVE_CALLING_CONVENTION_HPP

#if defined(_WIN32)
#define HE_CPP_STDCALL __stdcall
#define HE_CPP_CDECL __cdecl
#else
#define HE_CPP_STDCALL
#define HE_CPP_CDECL
#endif

#endif
