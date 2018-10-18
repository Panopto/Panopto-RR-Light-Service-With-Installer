using System;
using System.Diagnostics;
using System.Text;

namespace RRLightProgram
{
    public static class EnumUtils
    {
        // This utlitiy method is created to put Enum into Settings.settings.
        // There are several articles in the Internet that suggest it is possible,
        // but none actually worked. Therefore, use string in seettings and convert
        // to actual Enum value by this method.

        /// <summary>
        /// Convert string to Enum value.
        /// If input value does not represent any of Enum member, it throws exception.
        /// </summary>
        /// <typeparam name="TEnum">Enum type</typeparam>
        /// <param name="value">Enum value represented by string.</param>
        /// <returns>Enum value.</returns>
        public static TEnum ToEnum<TEnum>(this string value) where TEnum : struct
        {
            // The constraitn "where TEnum : struct" is a workaround to the problem that
            // we cannot specify Enum directly. See: https://stackoverflow.com/a/5067826

            TEnum result;
            if (Enum.TryParse(value, out result))
            {
                return result;
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("value {0} is invalid. It must be one of the followings:", value);
                sb.AppendLine();
                foreach (string name in Enum.GetNames(typeof(TEnum)))
                {
                    sb.AppendLine(name);
                }
                Trace.TraceError(sb.ToString());
                throw new ArgumentException(sb.ToString());
            }
        }

    }
}
