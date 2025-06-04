#ifndef FILE_HPP
#define FILE_HPP

#include "file-mode.hpp"
#include "file-stream.hpp"

class File {
public:
	static bool Exists(const char* fileName);

	static bool Delete(const char* fileName);

	static FileStream Open(const char* filePath, FileMode fileMode);
};

#endif // FILE_HPP
