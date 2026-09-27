using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimRound.Utilities
{
    public enum RRGasType : ushort
    {
        fatteningGas = 0,
        enbiggenerGas = 8,
        meldGas = 16,
        // flab grenades: the same swelling as fatteningGas, but the weight wears off
        temporaryFatteningGas = 24,
    }
}
