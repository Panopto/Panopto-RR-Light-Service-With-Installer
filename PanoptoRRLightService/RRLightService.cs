using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Threading;

namespace RRLightProgram
{
    public partial class RRLightService : ServiceBase
    {
        /// <summary>
        /// We accept the self signed server by overriding in ServerCertificateValidationCallback.
        /// </summary>
        private const bool SelfSignedServer = true;
        
        private static bool serverCertificateValidationCallbackIsSet = false;

        private MainLogic mainLogic = null;
        private Thread mainThread = null;

        public RRLightService()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Manually run the application for testing.
        /// </summary>
        public void ManualRun()
        {
            this.OnStart(null);
        }

        /// <summary>
        /// Start handler.
        /// MSDN documentaion recommends not to rely on args in general.
        /// We use config file for any parameters and ignore command line args.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            EnsureCertificateValidation();

            this.mainLogic = new MainLogic();
            this.mainThread = new Thread(new ThreadStart(mainLogic.ThreadMethod));
            this.mainThread.Start();
        }

        /// <summary>
        /// Stop handler. Waits for worker thread to stop.
        /// </summary>
        protected override void OnStop()
        {
            this.mainLogic.Stop();
            this.mainThread.Join();
            this.mainThread = null;
            this.mainLogic = null;
        }

        /// <summary>
        /// Ensures that our custom certificate validation has been applied
        /// </summary>
        public static void EnsureCertificateValidation()
        {
            if (RRLightService.SelfSignedServer && !RRLightService.serverCertificateValidationCallbackIsSet)
            {
                ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(CustomCertificateValidation);
                serverCertificateValidationCallbackIsSet = true;
            }
        }

        /// <summary>
        /// Ensures that server certificate is authenticated
        /// </summary>
        private static bool CustomCertificateValidation(object sender, X509Certificate cert, X509Chain chain, System.Net.Security.SslPolicyErrors error)
        {
            return true;
        }
    }
}