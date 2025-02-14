#include <iostream>
#include <fstream>
#include <vector>
#include <regex>
#include "libs/json.hpp"
#include <filesystem>

using json = nlohmann::json;
using namespace std;
namespace fs = std::filesystem;

struct Symbol {
    string type;
    string name;
    json members = json::array();
};

vector<Symbol> symbols;

void extractSymbolsFromFile(const string& filename) {
    ifstream file(filename);
    if (!file.is_open()) {
        cerr << "Failed to open file: " << filename << endl;
        return;
    }

    string line;
    regex classRegex(R"(class\s+(\w+))");
    regex structRegex(R"(struct\s+(\w+))");
    regex funcRegex(R"((\w+)\s+(\w+)\s*\((.*?)\))");
    regex enumRegex(R"(enum\s+(\w+))");
    regex varRegex(R"((\w+)\s+(\w+)\s*(=.*)?;)");

    while (getline(file, line)) {
        smatch match;

        if (regex_search(line, match, classRegex)) {
            Symbol classSymbol = { "class", match[1] };
            symbols.push_back(classSymbol);
        }
        else if (regex_search(line, match, structRegex)) {
            Symbol structSymbol = { "struct", match[1] };
            symbols.push_back(structSymbol);
        }
        else if (regex_search(line, match, funcRegex)) {
            Symbol funcSymbol = { "function", match[2] };
            funcSymbol.members.push_back({ {"parameters", match[3]} });
            symbols.push_back(funcSymbol);
        }
        else if (regex_search(line, match, enumRegex)) {
            Symbol enumSymbol = { "enum", match[1] };
            symbols.push_back(enumSymbol);
        }
        else if (regex_search(line, match, varRegex)) {
            Symbol varSymbol = { "variable", match[2] };
            symbols.push_back(varSymbol);
        }
    }
}

void to_json(json& j, const Symbol& s) {
    j = json{ {"type", s.type}, {"name", s.name}, {"members", s.members} };
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        cerr << "No file provided. Usage: symbol_extractor <file.cpp>" << endl;
        return 1;
    }

    string filename = argv[1];
    extractSymbolsFromFile(filename);

    string outputFileName = fs::path(filename).stem().string() + ".json";
    string outputPath = fs::path(filename).parent_path().string() + "/" + outputFileName;

    ofstream outFile(outputPath);
    outFile << json(symbols).dump(2);
    outFile.close();

    cout << "Extracted symbols saved to: " << outputPath << endl;
    return 0;
}
