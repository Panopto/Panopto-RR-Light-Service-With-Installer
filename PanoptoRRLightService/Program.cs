using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace RRLightProgram
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static void Main(string [] args)
        {
            var service = new RRLightService();

            if (Environment.UserInteractive)
            {
                // Application runs as an independent program (e.g. from console), but not as Windows service.

                // As this project uses Windows Form application as the base, it cannot run as console application.
                // Instead, all messages will be redirected to log file in "Logs" subcirectory.
                DirectoryInfo currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
                DirectoryInfo logsDirectory = currentDirectory.CreateSubdirectory("Logs");
                var listener = new TextWriterTraceListener(
                    Path.Combine(logsDirectory.FullName, string.Format("RRLightServiceDebug-{0:yy-MM-dd-HH-mm}.log", DateTime.UtcNow)));
                listener.TraceOutputOptions |= TraceOptions.DateTime;
                Trace.Listeners.Add(listener);
                Trace.AutoFlush = true;

                // Always enable verbose trace.
                TraceVerbose.Enabled = true;

                service.ManualRun();
            }
            else
            {
                // Send trace messages to Event Log.
                var listener = new EventLogTraceListener("PanoptoRRLightService");
                Trace.Listeners.Add(listener);

                // Enable verbose trace if config is set.
                TraceVerbose.Enabled = Properties.Settings.Default.EnableVerboseTrace;

                ServiceBase.Run(new ServiceBase[] { service });
            }
        }
    }
}