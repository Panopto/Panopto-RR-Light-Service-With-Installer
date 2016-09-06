using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RRLightProgram
{
    public static class TraceVerbose
    {
        /// <summary>
        /// Control flag to enable / disable verbose trace.
        /// </summary>
        public static bool Enabled { get; set; }

        /// <summary>
        /// Static contractor to disable verobse trace by default.
        /// </summary>
        static TraceVerbose()
        {
            TraceVerbose.Enabled = false;
        }

        /// <summary>
        /// Trace verbose message. It's logged at Infomation level.
        /// </summary>
        public static void Trace(string message)
        {
            if (TraceVerbose.Enabled)
            {
                System.Diagnostics.Trace.TraceInformation(message);
            }
        }

        /// <summary>
        /// Trace verbose message. It's logged at Infomation level.
        /// </summary>
        public static void Trace(string format, params object[] args)
        {
            if (TraceVerbose.Enabled)
            {
                System.Diagnostics.Trace.TraceInformation(format, args);
            }
        }
    }
}
