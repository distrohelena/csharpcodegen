#include "console.hpp"
#include <cstdio>

bool Console::Write(char* fileName)
{
    if (fileName == nullptr) {
        return false;
    }

    std::fputs(fileName, stdout);
    return true;
}

bool Console::Write(const std::string& text)
{
    std::fputs(text.c_str(), stdout);
    return true;
}

bool Console::WriteLine(char* fileName) 
{
    if (fileName == nullptr) {
        return false;
    }

    std::fputs(fileName, stdout);
    std::fputc('\n', stdout);
    return true;
}

bool Console::WriteLine(const std::string& text)
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

std::string Console::ReadLine()
{
    char buffer[4096];
    if (std::fgets(buffer, sizeof(buffer), stdin) == nullptr) {
        return std::string();
    }

    std::string line(buffer);
    while (!line.empty() && (line.back() == '\n' || line.back() == '\r')) {
        line.pop_back();
    }

    return line;
}
