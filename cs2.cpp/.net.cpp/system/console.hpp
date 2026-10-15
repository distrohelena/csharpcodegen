#ifndef CONSOLE_HPP
#define CONSOLE_HPP
#include "../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/console.hpp requires HE_CPP_USE_STD_STRING=1; console services have not been adapted to custom string storage."
#endif


#include <string>
#include "../runtime/native_runtime.hpp"


class Console {
public:
	static bool Write(const char* fileName);
	static bool Write(const HeCppString& text);
	static bool WriteLine(const char* fileName);
	static bool WriteLine(const HeCppString& text);
	static bool WriteLine();
	static HeCppString ReadLine();
};

#endif // CONSOLE_HPP
