using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cs2.core {
    [Flags]
    public enum ParameterModifier {
        None = 0,
        In = 1 << 0,
        Out = 1 << 1,
        Ref = 1 << 2,
        Params = 1 << 3,
        This = 1 << 4,
    }
}
