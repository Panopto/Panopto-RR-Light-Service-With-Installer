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
            if (args.Length > 0
                && args[0] == "-runFromConsole")
            {
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