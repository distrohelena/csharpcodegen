#include "file.hpp"
#include <fstream>

bool File::Exists(const char* fileName) {
	if (!fileName)
	{
		return false;
	}

	std::ifstream file(fileName);
	return file.good();
}

bool File::Delete(const char* fileName) {
	if (!fileName)
	{
		return false;
	}

	return std::remove(fileName) == 0;
}

FileStream File::Open(const char* filePath, FileMode fileMode)
{

}
