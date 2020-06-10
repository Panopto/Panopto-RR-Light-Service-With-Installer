using System;
using System.Diagnostics;
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

        private StateMachine stateMachine = null;
        private LightServiceTether lightServiceTether = null;
        private RemoteRecorderSync remoteRecorderSync = null;

        private DelcomLight delcomLight = null;
        private SwivlChicoLight chicoLight = null;
        private SerialComm serialComm = null;
        private KuandoLight kuandoLight = null;

        private Thread setupLightThread;
        private CancellationTokenSource cts;
        private readonly int closeWaitTime = Properties.Settings.Default.WaitTimeForClose;

        /// <summary>
        /// Constructor.
        /// </summary>
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
        protected override void OnStart(string[] args)
        {
            EnsureCertificateValidation();

            this.stateMachine = new StateMachine();
            this.lightServiceTether = new LightServiceTether();
            this.lightServiceTether.SetupUserTether();

            // Find light and recorder in a seperate thread
            // So that LS can start without a light or recorder detected
            this.cts = new CancellationTokenSource();
            
            this.setupLightThread = new Thread(FindLight);
            setupLightThread.Start();

            Trace.TraceInformation("Started program");
        }

        /// <summary>
        /// Stop handler.
        /// </summary>
        protected override void OnStop()
        {
            this.cts.Cancel();

            if (this.remoteRecorderSync != null)
            {
                this.remoteRecorderSync.Stop();
                this.remoteRecorderSync = null;
            }

            if (this.delcomLight != null)
            {
                this.delcomLight.Stop();
                this.delcomLight = null;
            }

            if (this.chicoLight != null)
            {
                this.chicoLight.Stop();
                this.chicoLight = null;
            }

            if (this.serialComm != null)
            {
                this.serialComm.Stop();
                this.serialComm = null;
            }

            if (this.kuandoLight != null)
            {
                this.kuandoLight.Stop();
                this.kuandoLight = null;
            }

            if (this.stateMachine != null)
            {
                this.stateMachine.Stop();
                this.stateMachine = null;
            }

            this.lightServiceTether.StopUserTether();

            // Try to close the thread for X seconds, else abort (X set in config)
            if (!this.setupLightThread.Join(TimeSpan.FromSeconds(closeWaitTime)))
            {
                this.setupLightThread.Abort();
            }
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

        private void FindLight()
        {
            ILightControl lightControl = null;
            IInputResultReceiver resultReceiver = null;
            CancellationToken token = this.cts.Token;

            // 1 each 5 seconds -> 60 / 5 =  12 a minute -> 60 * 12 = 720 an hour
            // Will log a warning every hour when a light couldn't be found
            int numOfChecks = 720;

            this.remoteRecorderSync = new RemoteRecorderSync((IStateMachine)this.stateMachine, this.lightServiceTether);

            while (lightControl == null && !token.IsCancellationRequested)
            {
                if (string.Equals(Properties.Settings.Default.DeviceType, "Delcom", StringComparison.OrdinalIgnoreCase))
                {
                    // Set up of Delcom light (with button) device.
                    this.delcomLight = new DelcomLight((IStateMachine)this.stateMachine);
                    lightControl = this.delcomLight as ILightControl;

                    if (this.delcomLight.Start())
                    {
                        Trace.TraceInformation("Service started with Delcom light.");
                    }
                    else
                    {
                        if (numOfChecks >= 720)
                        {
                            Trace.TraceWarning("Failed to start up Delcom component, will keep trying every 5 seconds");
                            numOfChecks = 0;
                        }
                        lightControl = null;
                        this.delcomLight = null;
                    }
                }
                else if (string.Equals(Properties.Settings.Default.DeviceType, "SwivlChico", StringComparison.OrdinalIgnoreCase))
                {
                    // Set up of SwivlChico light (with button) device.
                    this.chicoLight = new SwivlChicoLight((IStateMachine)this.stateMachine);
                    lightControl = this.chicoLight as ILightControl;

                    if (this.chicoLight.Start())
                    {
                        Trace.TraceInformation("Service started with Swivl Chico.");
                    }
                    else
                    {
                        if (numOfChecks >= 720)
                        {
                            Trace.TraceWarning("Failed to start up SwivlChico component, will keep trying every 5 seconds");
                            numOfChecks = 0;
                        }
                        lightControl = null;
                        this.chicoLight = null;
                    }
                }
                else if (string.Equals(Properties.Settings.Default.DeviceType, "Serial", StringComparison.OrdinalIgnoreCase))
                {
                    // Set up serial port
                    this.serialComm = new SerialComm((IStateMachine)this.stateMachine);
                    resultReceiver = this.serialComm as IInputResultReceiver;

                    if (!this.serialComm.Start(this.remoteRecorderSync))
                    {
                        Trace.TraceError("Failed to start up Serial component. Terminate.");
                        throw new ApplicationException("Failed to start up Serial component. Terminate.");
                    }
                }
                else if (string.Equals(Properties.Settings.Default.DeviceType, "Kuando", StringComparison.OrdinalIgnoreCase))
                {
                    this.kuandoLight = new KuandoLight((IStateMachine)this.stateMachine);
                    lightControl = this.kuandoLight as ILightControl;

                    if (this.kuandoLight.Start())
                    {
                        Trace.TraceInformation("Kuando Busylight service started");
                    }
                    else
                    {
                        if (numOfChecks >= 720)
                        {
                            Trace.TraceWarning("Failed to start up Kuando component, will keep trying every 5 seconds");
                            numOfChecks = 0;
                        }
                        lightControl = null;
                        this.kuandoLight = null;
                    }
                }
                // TODO: add here for device specific start up when another device type is added.
                else
                {
                    throw new InvalidOperationException("Specified device type is not supported: " + Properties.Settings.Default.DeviceType);
                }
                // Check every 5 seconds for light input
                if (lightControl == null)
                {
                    Thread.Sleep(5000);
                    numOfChecks++;
                }
            }
            // Start processing of the state machine.
            this.stateMachine.Start(this.remoteRecorderSync, lightControl, resultReceiver);
        }
    }
}