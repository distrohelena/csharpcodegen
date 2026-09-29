# C# to (Any) Language Converter

Supported Languages
- C++ (cs2.cpp)
- TypeScript (cs2.ts)

Core Project Features
- Base classes for conversion


## C# to TypeScript

### Reproducible dependency setup

The TypeScript converter uses the `submodules/nucleusdotnet` Git submodule. Initialize it at the revision recorded by this repository; a sibling checkout is not an implicit dependency:

```powershell
git submodule update --init -- submodules/nucleusdotnet
npm ci --prefix cs2.ts/.net.ts
dotnet restore cs2.ts.tests/cs2.ts.tests.csproj -p:TargetFramework=net9.0
dotnet build cs2.ts/cs2.ts.csproj --no-restore
dotnet test cs2.ts.tests/cs2.ts.tests.csproj --no-restore
```

The submodule revision pins Nucleus .NET 1.0.4 (`9fb31812af39b9a1d4cb8eb785a4f387baf6e388`). Runtime metadata tooling uses the committed npm lockfile. Install the SDK/runtime needed for the project's `net9.0` target before building. Select TargetFramework=net9.0 for the converter restore; Nucleus also supports .NET Framework 4.7.2, which is not used by this converter. Do not set TargetFrameworks globally: that changes single-target project restore evaluation and can prevent the xUnit adapter from loading. Require an actual nonzero test count, not only a zero process exit code. Clone/build verification must start without inherited `bin`, `obj` or runtime `node_modules` directories.

The native FormatException adapter and its runtime catalog/project entries are versioned together. Application drivers may add their documented runtime normalizations after copying shims; SweetSquare supplies the C# `Message` getter through its exception normalizer.


#### Why?
In the process of developing a networking package, I needed to connect a client written in C# to a web one. 

Using webasm, though it is a possible path, would make the library harder to deal with.

With this converter I can develop applications in C# and use them on my TypeScript websites, guaranteeing that they are always up to date and with easy access to all interfaces and methods (no exports/extern access needed, the result is always native TypeScript). 

Features
- BinaryReader/BinaryWriter
- MemoryStream
- Delegates (w/ Generics support)
- Function overload
- Classes inside classes
- Multiple constructors
- Multiple functions with same name
- (some) C# 12 features
- Pass argument by out/ref 
    - Dynamically creates an object that is passed to the method, then assigns back to the value name that was passed. 
- Using statements (using BinaryReader = ...)

#### .NET Library System
    - Action<T...>
    - DateTime
    - Guid
    - NotSupportedException
    - TimeSpan
#### System.Collections.Concurrent
    - ConcurrentDictionary<Key, Value>
#### System.Collections.Generic
    - Dictionary<Key, Value>
    - List<T>
    - SortedList<T>
#### System.Drawing
    - Rectangle

### Not Implemented
- Function arguments with same name as classes variables
    - A bit harder
- No constructor creation (argument-less)
- .Invoke() on delegates
- Class Functions with Arrow declaration
- Reflection (but it's getting there)
- Generic classes with shared name (i.e. RequestResult and RequestResult<T>)
- Override generic (i.e. public class ListData : List<Data> { } )
- Class inside class with generic parameters (so base class is <T>, but subclass is not)
- Static functions with generic parameters
    - TypeScript does not natively support this
    - There are workarounds but nothing implemented
- Tuples (public (string key, int value) MethodName())
- In-line out keyword (just lazy not hard to implement)



## C# to C++

#### Why?

#### Installing
cs2.cpp needs doxygen available to the `codegen` CLI.

The CLI resolves doxygen in this order:
- `CS2_DOXYGEN_PATH`
- `DOXYGEN_PATH`
- a bundled `doxygen` next to the CLI executable
- `PATH`
- common Windows install locations

If none of those exist, the conversion run will fail with a clear error telling you what to set.

#### Worker threads
The converter lowers classes and analyzes ownership on dedicated worker threads.

`--set codegen-worker-threads=N` sets the count (default: logical processor count; `1` forces sequential; output is byte-identical at any count).
