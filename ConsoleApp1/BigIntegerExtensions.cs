using System;
using System.Numerics;

namespace ConsoleApp1
{
    public static class BigIntegerExtensions
    {
        public static string ToIp(this BigInteger big)
        {
            var bytes = new byte[4];
            var span = bytes.AsSpan();
            var d = big.TryWriteBytes(span, out var i, true, true);
            if (!d) return null;
            return i switch
            {
                1 => $"0.0.0.{bytes[0]}",
                2 => $"0.0.{bytes[0]}.{bytes[1]}",
                3 => $"0.{bytes[0]}.{bytes[1]}.{bytes[2]}",
                4 => $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}",
                _ => null
            };
        }
    }
}