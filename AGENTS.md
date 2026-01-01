# Repository Guidelines

- One class per file.
- Add XML comments to every class, function and or property. Make sure the code is as easy as possible to be read by humans.
- XML comments must be substantive (avoid placeholder text) and explain the role/behavior. All members (fields, properties, methods, constructors) should have XML comments.
- All projects use the default Global Usings; no need to keep adding back imports.
- Order members using standard C# layout (constants/fields, constructors, properties, methods) to keep files predictable.
- Do not add redundant `private` modifiers; members without an access modifier are assumed private in C#.
- All fields should be PascalCase.
- C# code should follow TypeScript indentation rules for end and close brackets, and opening braces should be on the same line as declarations or control statements.
- Do not use tuples.
- Follow MVC: keep logic in separate classes (controllers/services/managers) and keep UI classes focused only on presentation and input wiring.
- Avoid half-measures that patch broken state; ensure systems are correctly initialized or fix the underlying cause instead of bolting on runtime fixes.
- Do not create local helper functions; if a helper is needed, add it to the appropriate Utils class or to a related type.
- Do not create default values when a valid value is required; throw exceptions instead of silently constructing default.
- Avoid using `??` for exception handling; validate inputs before assignment so error causes stay clear.
- Prefer `else if` for sequential conditional checks that are mutually exclusive; avoid stacked `if` statements when only one branch should be evaluated.
- Nullable reference types are disabled; do not use nullable annotations or nullable patterns in code.
