using System;
using System.Collections.Generic;
using System.Text;

namespace BotFrameworkTwitterAdapter.Extensions
{
    public static class StringExtension
    {
        public static string SafeSubstring(this string value, int startIndex, int length)
        {
            if (startIndex <= value.Length)
            {
                if (startIndex + length <= value.Length)
                {
                    return value.Substring(startIndex, length);
                }
                return value.Substring(startIndex);
            }
            return string.Empty;
        }
    }
}
