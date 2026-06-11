#include "file.hpp"

#include "helcpp_config.hpp"

#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER
#endif

#include <fstream>

bool File::Exists(const char* fileName) {
	if (!fileName)
	{
		return false;
	}

#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
	if (HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(fileName)) {
		return HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::Exists(fileName);
	}
#endif

	std::ifstream file(fileName);
	return file.good();
}

bool File::Exists(const std::string& fileName) {
	return Exists(fileName.c_str());
}

bool File::Delete(const char* fileName) {
	if (!fileName)
	{
		return false;
	}

	return std::remove(fileName) == 0;
}

bool File::Delete(const std::string& fileName) {
	return Delete(fileName.c_str());
}

FileStream File::Open(const char* filePath, FileMode fileMode)
{
	return FileStream(filePath, fileMode);
}

FileStream File::Open(const std::string& filePath, FileMode fileMode)
{
	return Open(filePath.c_str(), fileMode);
}

FileStream* File::OpenRead(const char* filePath)
{
#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
	if (HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(filePath)) {
		return HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::OpenRead(filePath);
	}
#endif

	return new FileStream(filePath, FileMode::Open, FileAccess::Read, FileShare::Read);
}

FileStream* File::OpenRead(const std::string& filePath)
{
	return OpenRead(filePath.c_str());
}
