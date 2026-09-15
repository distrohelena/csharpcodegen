#ifndef FILE_HPP
#define FILE_HPP
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/io/file.hpp requires HE_CPP_USE_STD_STRING=1; file paths have not been adapted to custom string storage."
#endif


#include "file-mode.hpp"
#include "file-stream.hpp"
#include <string>
#include "../../runtime/native_runtime.hpp"


class File {
public:
	static bool Exists(const char* fileName);
	static bool Exists(const std::string& fileName);

	static bool Delete(const char* fileName);
	static bool Delete(const std::string& fileName);

	static FileStream Open(const char* filePath, FileMode fileMode);
	static FileStream Open(const std::string& filePath, FileMode fileMode);
	static FileStream* OpenRead(const char* filePath);
	static FileStream* OpenRead(const std::string& filePath);
};

#endif // FILE_HPP
