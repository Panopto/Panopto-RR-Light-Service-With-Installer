using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.ServiceProcess;

namespace RRLightProgram
{
    internal static class Program
    {
        /// <summary>
        /// Flag to indicate that the program runs in console mode (not Windows service).
        /// </summary>
        public static bool RunFromConsole = false;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static void Main(string [] args)
        {
            if (args.Length > 0 && string.Compare(args[0], "-runFromConsole", StringComparison.OrdinalIgnoreCase) == 0)
            {
                RunFromConsole = true;

                //Create "Logs" directory if it doesn't exist
                DirectoryInfo di = new DirectoryInfo(Directory.GetCurrentDirectory());
                di.CreateSubdirectory("Logs");

                //Set up a text trace listener to output debug information to file.
                var listner = new TextWriterTraceListener(Directory.GetCurrentDirectory() + "\\Logs\\RRLightServiceDebug-" + DateTime.Now.ToString("yy-MM-dd") + DateTime.Now.ToString(".hh-mm-ss") + ".log", "PanoptoRRLightConsole");
                listner.TraceOutputOptions |= TraceOptions.DateTime;
                Trace.Listeners.Add(listner);
                Trace.AutoFlush = true;

                Trace.TraceInformation("RRLightProgram started with -runFromConsole flag.");
                var lightService = new RRLightService();
                lightService.ManualRun();
            }
            else
            {
                // Set up event log trace listner.
                var listner = new EventLogTraceListener("PanoptoRRLightService");
                Trace.Listeners.Add(listner);

                Trace.TraceInformation("RRLightService is going to start.");
                var lightService = new RRLightService();
                ServiceBase.Run(new ServiceBase[] { lightService });
            }
        }
    }
}