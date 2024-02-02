using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MercuryChecker;

public class Utils
{
    public static int Modulo(int x, int m)
    {
        return (x % m + m) % m;
    }

    public static float Modulo(float x, int m)
    {
        return (x % m + m) % m;
    }
}
