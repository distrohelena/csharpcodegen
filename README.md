# C# to (Any) Language Converter

Supported Languages
- C++ (cs2.cpp)
- TypeScript (cs2.ts)

Core Project Features
- Base classes for conversion


## C# to TypeScript

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
cs2.cpp needs doxygen on your PATH