using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public static class Num
    {
        public static int ArrayToInt(byte[] buffer, int offset, int size)
        {
            int result = 0;
            for (int i = 0; i < size; i++)
                result += buffer[offset + i] << (i * 8);
            return result;
        }

        public static uint ArrayToUInt(byte[] buffer, int offset, int size)
        {
            return (uint)ArrayToInt(buffer, offset, size);
        }

        public static long ArrayToLong(byte[] buffer, long offset, int size)
        {
            long result = 0;
            for (int i = 0; i < size; i++)
                result += (long)buffer[offset + i] << (i * 8);
            return result;
        }

        public static ulong ArrayToULong(byte[] buffer, long offset, int size)
        {
            return (ulong)ArrayToLong(buffer, offset, size);
        }

        public static string ArrayToHexString(byte[] buffer, int offset, int size)
        {
            return BitConverter.ToString(buffer, offset, size).Replace("-", "");
        }
    }
}
