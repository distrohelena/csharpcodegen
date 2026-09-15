#ifndef PATH_HPP
#define PATH_HPP
#include "../../runtime/native_runtime.hpp"







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

    static HeCppString Combine(const HeCppString& left, const HeCppString& right);

    static HeCppString Combine(const HeCppString& first, const HeCppString& second, const HeCppString& third);

    static HeCppString GetDirectoryName(const HeCppString& path);

    static HeCppString GetFileName(const HeCppString& path);

    static HeCppString GetFullPath(const HeCppString& path);

    static HeCppString ChangeExtension(const HeCppString& path, const HeCppString& extension);

    static bool IsPathRooted(const HeCppString& path);
};

#endif // PATH_HPP
