#include "../helcpp_config.hpp"
#if !defined(HE_CPP_GENERATED_CONFIG) || defined(HE_CPP_REQ_CONSOLE)
#include "console.hpp"
#include <cstdio>

bool Console::Write(const char* fileName)
{
    if (fileName == nullptr) {
        return false;
    }

    std::fputs(fileName, stdout);
    return true;
}

bool Console::Write(const HeCppString& text)
{
    std::fputs(text.c_str(), stdout);
    return true;
}

bool Console::WriteLine(const char* fileName)
{
    if (fileName == nullptr) {
        return false;
    }

    std::fputs(fileName, stdout);
    std::fputc('\n', stdout);
    return true;
}

bool Console::WriteLine(const HeCppString& text)
{
    std::fputs(text.c_str(), stdout);
    std::fputc('\n', stdout);
    return true;
}

bool Console::WriteLine()
{
    std::fputc('\n', stdout);
    return true;
}

HeCppString Console::ReadLine()
{
    char buffer[4096];
    if (std::fgets(buffer, sizeof(buffer), stdin) == nullptr) {
        return HeCppString();
    }

    HeCppString line(buffer);
    while (!line.empty() && (line.back() == '\n' || line.back() == '\r')) {
        line.pop_back();
    }

    return line;
}

#endif
