#include "../../helcpp_config.hpp"
#if !defined(HE_CPP_GENERATED_CONFIG) || defined(HE_CPP_REQ_FILE)
#include "file.hpp"

#include "helcpp_config.hpp"

#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER
#endif

#include <sys/stat.h>

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

	struct stat status;
	return stat(fileName, &status) == 0;
}

bool File::Exists(const HeCppString& fileName) {
	return Exists(fileName.is_null() ? nullptr : fileName.c_str());
}

bool File::Delete(const char* fileName) {
	if (!fileName)
	{
		return false;
	}

	return std::remove(fileName) == 0;
}

bool File::Delete(const HeCppString& fileName) {
	return Delete(fileName.is_null() ? nullptr : fileName.c_str());
}

FileStream File::Open(const char* filePath, FileMode fileMode)
{
	return FileStream(filePath, fileMode);
}

FileStream File::Open(const HeCppString& filePath, FileMode fileMode)
{
	if (filePath.is_null()) {
		he_cpp_raise(ArgumentNullException("filePath"));
	}
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

FileStream* File::OpenRead(const HeCppString& filePath)
{
	if (filePath.is_null()) {
		he_cpp_raise(ArgumentNullException("filePath"));
	}
	return OpenRead(filePath.c_str());
}

#endif
