using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.ServiceProcess;

namespace RRLightProgram
{
    internal static class Program
    {
        public static bool RunFromConsole = false;
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static void Main(string [] args)
        {
            if (args.Length > 0
                && args[0] == "-runFromConsole")
            {
                RunFromConsole = true;

                //Create "Logs" directory if it doesn't exist
                DirectoryInfo di = new DirectoryInfo(Directory.GetCurrentDirectory());
                di.CreateSubdirectory("Logs");

                //Set up a trace listener to output debug information to file
                Trace.Listeners.Add(new TextWriterTraceListener(Directory.GetCurrentDirectory() + "\\Logs\\RRLightServiceDebug-" + DateTime.Now.ToString("yy-MM-dd") + DateTime.Now.ToString(".hh-mm-ss") + ".log", "RRLightServiceListener"));
                Trace.TraceInformation("Service running with -runFromConsole flag. Started at {0}.", DateTime.Now);
                Trace.Flush();

                RRLightService lightService = new RRLightService();
                lightService.ManualRun();
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new RRLightService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}