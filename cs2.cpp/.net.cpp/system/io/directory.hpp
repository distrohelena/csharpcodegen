#pragma once
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/io/directory.hpp requires HE_CPP_USE_STD_STRING=1; directory paths have not been adapted to custom string storage."
#endif

#if !HE_CPP_USE_STD_VECTOR
#error "system/io/directory.hpp requires HE_CPP_USE_STD_VECTOR=1; directory path traversal still uses std::vector."
#endif


#include <string>
#include "../../runtime/native_runtime.hpp"



class Directory {
public:
    static bool Exists(const std::string& path);
    static void CreateDirectory(const std::string& path);
};
