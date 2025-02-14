#include "file.hpp"
#include <fstream>

bool File::Exists(char* fileName) {
	if (!fileName)
	{
		return false;
	}

	std::ifstream file(fileName);
	return file.good();
}

bool File::Delete(char* fileName) {
	if (!fileName)
	{
		return false;
	}

	return std::remove(fileName) == 0;
}

FileStream File::Open(char* filePath, FileMode fileMode)
{

}