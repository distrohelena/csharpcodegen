const ts = require("typescript");
const fs = require("fs");
const path = require("path");

// Get file path from arguments
const fileName = process.argv[2];
if (!fileName) {
    console.error("No file provided. Usage: node extractSymbols.js <file>");
    process.exit(1);
}

// Read the TypeScript file
const sourceCode = fs.readFileSync(fileName, "utf8");

// Parse the TypeScript file
const sourceFile = ts.createSourceFile(fileName, sourceCode, ts.ScriptTarget.Latest, true);

const symbols = [];

// Traversal function
function visit(node) {
    // Extract class declarations
    if (ts.isClassDeclaration(node) && node.name) {
        const className = node.name.text;
        const classMembers = [];

        // Traverse class members (methods, properties, accessors)
        node.members.forEach(member => {
            if (ts.isMethodDeclaration(member) && member.name) {
                classMembers.push({
                    type: "method",
                    name: member.name.getText(),
                    parameters: member.parameters.map(param => ({
                        name: param.name.getText(),
                        type: param.type ? param.type.getText() : "any",
                    })),
                    returnType: member.type ? member.type.getText() : "void",
                });
            }

            if (ts.isPropertyDeclaration(member) && member.name) {
                classMembers.push({
                    type: "property",
                    name: member.name.getText(),
                    propertyType: member.type ? member.type.getText() : "any",
                });
            }

            if (ts.isGetAccessorDeclaration(member) && member.name) {
                classMembers.push({
                    type: "getter",
                    name: member.name.getText(),
                    returnType: member.type ? member.type.getText() : "any",
                });
            }

            if (ts.isSetAccessorDeclaration(member) && member.name) {
                classMembers.push({
                    type: "setter",
                    name: member.name.getText(),
                    parameters: member.parameters.map(param => ({
                        name: param.name.getText(),
                        type: param.type ? param.type.getText() : "any",
                    })),
                });
            }
        });

        symbols.push({
            type: "class",
            name: className,
            members: classMembers,
        });
    }

    // Extract interface declarations
    if (ts.isInterfaceDeclaration(node) && node.name) {
        const interfaceName = node.name.text;
        const interfaceMembers = [];

        // Traverse interface members
        node.members.forEach(member => {
            if (ts.isPropertySignature(member) && member.name) {
                interfaceMembers.push({
                    type: "property",
                    name: member.name.getText(),
                    propertyType: member.type ? member.type.getText() : "any",
                });
            }

            if (ts.isMethodSignature(member) && member.name) {
                interfaceMembers.push({
                    type: "method",
                    name: member.name.getText(),
                    parameters: member.parameters.map(param => ({
                        name: param.name.getText(),
                        type: param.type ? param.type.getText() : "any",
                    })),
                    returnType: member.type ? member.type.getText() : "void",
                });
            }
        });

        symbols.push({
            type: "interface",
            name: interfaceName,
            members: interfaceMembers,
        });
    }

    // Extract enum declarations
    if (ts.isEnumDeclaration(node)) {
        const enumName = node.name.text;
        const enumMembers = node.members.map(member => ({
            name: member.name.getText(),
            value: member.initializer ? member.initializer.getText() : null,
        }));

        symbols.push({
            type: "enum",
            name: enumName,
            members: enumMembers,
        });
    }

    // Extract standalone function declarations
    if (ts.isFunctionDeclaration(node) && node.name) {
        symbols.push({
            type: "function",
            name: node.name.text,
            parameters: node.parameters.map(param => ({
                name: param.name.getText(),
                type: param.type ? param.type.getText() : "any",
            })),
            returnType: node.type ? node.type.getText() : "void",
        });
    }

    // Extract standalone variable declarations
    if (ts.isVariableDeclaration(node) && node.name) {
        symbols.push({
            type: "variable",
            name: node.name.getText(),
            variableType: node.type ? node.type.getText() : "any",
        });
    }

    ts.forEachChild(node, visit); // Recursively visit child nodes
}

// Start traversal
visit(sourceFile);

// Save the output to a JSON file
const outputFileName = `${path.basename(fileName, path.extname(fileName))}.json`;
const outputPath = path.join(path.dirname(fileName), outputFileName);

fs.writeFileSync(outputPath, JSON.stringify(symbols, null, 2), "utf8");
console.log(`Extracted symbols saved to: ${outputPath}`);
