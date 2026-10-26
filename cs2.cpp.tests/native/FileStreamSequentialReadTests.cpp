#include <windows.h>
#include "system/io/file-stream.hpp"
#include <chrono>
#include <fstream>
#include <iostream>
#include <vector>

/// <summary>Counts actual stdio seek calls at the native import boundary, independently of elapsed-time scheduling.</summary>
static unsigned long long SeekCalls=0;
/// <summary>Forwards each intercepted seek to the real UCRT export without recursively using the executable's import slot.</summary>
static int __cdecl CountedSeek(std::FILE* file,long offset,int origin) {
    using SeekFunction=int(__cdecl*)(std::FILE*,long,int);
    static SeekFunction original=reinterpret_cast<SeekFunction>(GetProcAddress(GetModuleHandleW(L"ucrtbase.dll"),"fseek"));
    if(!original) throw std::runtime_error("The native stdio seek export was not available.");
    ++SeekCalls;
    return original(file,offset,origin);
}
/// <summary>Routes this test executable's fseek import through the counting forwarder; production runtime headers remain unchanged.</summary>
extern "C" { int(__cdecl* __imp_fseek)(std::FILE*,long,int)=CountedSeek; }
/// <summary>Verifies sequential binary reads remain buffered while explicit positioning, writes, flushing and resizing retain correct data.</summary>
int main(int argc,char** argv) {
    if(argc!=2) return 2;
    const char* path=argv[1];
    std::vector<uint8_t> data(200000);
    for(size_t index=0;index<data.size();++index) data[index]=static_cast<uint8_t>(index%251);
    {std::ofstream output(path,std::ios::binary); output.write(reinterpret_cast<const char*>(data.data()),data.size());}
    try {
        FileStream stream(path,FileMode::Open,FileAccess::ReadWrite,FileShare::ReadWrite);
        unsigned long long before=SeekCalls;

        auto start=std::chrono::steady_clock::now();
        uint8_t bytes[8]{};
        for(size_t index=0;index<100000;++index) {
            stream.SetPosition(index*2);
            if(stream.Read(bytes,0,2)!=2 || bytes[0]!=data[index*2] || bytes[1]!=data[index*2+1]) return 4;
        }
        double elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();

        auto operations=SeekCalls-before;
        std::cout<<"100000 sequential 2-byte reads: "<<elapsed<<" ms; native seeks="<<operations<<"\n";
        stream.SetPosition(7);
        if(stream.Read(bytes,0,2)!=2 || bytes[0]!=7 || bytes[1]!=8) return 6;
        if(stream.Seek(-2,SeekOrigin::Current)!=7 || stream.Read(bytes,0,1)!=1 || bytes[0]!=7) return 7;
        stream.SetPosition(4); uint8_t replacement[2]{171,205}; stream.Write(replacement,0,2);
        stream.SetPosition(3);
        if(stream.Read(bytes,0,5)!=5 || bytes[0]!=3 || bytes[1]!=171 || bytes[2]!=205 || bytes[3]!=6 || bytes[4]!=7) return 8;
        stream.Flush();
        if(stream.Read(bytes,0,1)!=1 || bytes[0]!=8) return 9;
        stream.SetLength(16); stream.SetPosition(14);
        if(stream.Read(bytes,0,4)!=2 || bytes[0]!=14 || bytes[1]!=15 || stream.Read(bytes,0,1)!=0) return 10;
        stream.SetLength(32);
        if(stream.Read(bytes,0,2)!=2 || bytes[0]!=0 || bytes[1]!=0) return 11;
        stream.SetPosition(31);
        if(stream.Read(bytes,0,2)!=1 || stream.Position()!=32) return 15;
        auto seeksAtEof=SeekCalls;
        {
            std::ofstream output(path,std::ios::binary|std::ios::app);
            const char appended[2]{43,44};
            output.write(appended,2);
        }
        if(stream.Read(bytes,0,2)!=2 || bytes[0]!=43 || bytes[1]!=44) {
            std::cerr<<"A reader at EOF did not observe an external append.\n";
            return 16;
        }
        if(SeekCalls==seeksAtEof) {std::cerr<<"The EOF indicator was not cleared by repositioning before the next read.\n";return 17;}
        stream.SetPosition(0);
        if(stream.Read(bytes,2,2)!=2 || bytes[2]!=0 || bytes[3]!=1 || stream.Position()!=2) return 12;
        // A buffered scan must not turn every scalar into a native seek/query request.
        if(operations>64) {std::cerr<<"Sequential scalar reads repeatedly repositioned the native file.\n";return 13;}
    } catch(const std::exception& error) {std::cerr<<error.what()<<"\n";return 14;}
    std::cout<<"FileStream sequential-read, positioning, read/write direction, flush, resize and EOF checks passed.\n";
    return 0;
}