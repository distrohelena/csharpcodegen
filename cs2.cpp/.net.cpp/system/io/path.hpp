#ifndef PATH_HPP
#define PATH_HPP
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/io/path.hpp requires HE_CPP_USE_STD_STRING=1; path operations have not been adapted to custom string storage."
#endif

#if !HE_CPP_USE_STD_VECTOR
#error "system/io/path.hpp requires HE_CPP_USE_STD_VECTOR=1; path normalization still uses std::vector."
#endif


#include <string>
#include "../../runtime/native_runtime.hpp"



class Path {
public:
    #ifdef _WIN32
    static constexpr char DirectorySeparatorChar = '\\';
    static constexpr char AltDirectorySeparatorChar = '/';
    #else
    static constexpr char DirectorySeparatorChar = '/';
    static constexpr char AltDirectorySeparatorChar = '\\';
    #endif

    static std::string Combine(const std::string& left, const std::string& right);

    static std::string Combine(const std::string& first, const std::string& second, const std::string& third);

    static std::string GetDirectoryName(const std::string& path);

    static std::string GetFileName(const std::string& path);

    static std::string GetFullPath(const std::string& path);

    static std::string ChangeExtension(const std::string& path, const std::string& extension);

    static bool IsPathRooted(const std::string& path);
};

#endif // PATH_HPP
