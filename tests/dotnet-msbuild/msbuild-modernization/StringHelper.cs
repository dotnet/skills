using System;
using System.Linq;

namespace LegacyApp
{
    public static class StringHelper
    {
        public static string Reverse(string input)
        {
            return new string(input.Reverse().ToArray());
        }
    }
}
