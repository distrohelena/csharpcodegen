#ifndef FILE_HPP
#define FILE_HPP

#include "file-mode.hpp"
#include "file-stream.hpp"

class File {
public:
	static bool Exists(char* fileName);

	static bool Delete(char* fileName);

	static FileStream Open(char* filePath, FileMode fileMode);
};

#endif // FILE_HPP
