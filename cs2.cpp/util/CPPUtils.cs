using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    public static class CPPUtils {
        public static string GetInheritance(ConvertedProgram program, ConvertedClass cl) {
            string implements = "";
            string extends = "";

            List<string> exts = new List<string>();
            List<string> impls = new List<string>();

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];

                var extCl = program.Classes.FirstOrDefault(c => c.Name == ext);
                //if (extCl == null) {
                //    var knownClass = ((CPPProgram)program).Requirements.FirstOrDefault(c => c.Name == ext);

                //    if (knownClass == null) {
                //        //throw new Exception($"Class not found: {ext}");
                //    }
                //}

                //if (extCl?.DeclarationType == MemberDeclarationType.Interface ||
                //    cl.DeclarationType == MemberDeclarationType.Interface) {
                //    impls.Add(ext);
                //} else {
                //    exts.Add(ext);
                //}
            }

            return "";
        }
    }
}
