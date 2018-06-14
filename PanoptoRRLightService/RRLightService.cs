using System;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;

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

        private RemoteRecorderSync remoteRecorderSync = null;

        private DelcomLight delcomLight = null;
        private SwivlChicoLight chicoLight = null;
        private SerialComm serialComm = null;
        private KuandoLight kuandoLight = null;

        /// <summary>
        /// Constrocutor.
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

            ILightControl lightControl = null;
            IInputResultReceiver resultReceiver = null;

            this.stateMachine = new StateMachine();

            this.remoteRecorderSync = new RemoteRecorderSync((IStateMachine)this.stateMachine);
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
                    // It is desired to block starting the service, but that prevents the installer completes
                    // when the device is not installed. (vital=yes causes failure, vital=no becomes hung.)
                    // As a workaround, service continues to start, but with error log message.
                    Trace.TraceError("Failed to start up Delcom Light component. Service continues to run, but the device is not recognized without restart of the service.");
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
                    // It is desired to block starting the service, but that prevents the installer completes
                    // when the device is not installed. (vital=yes causes failure, vital=no becomes hung.)
                    // As a workaround, service continues to start, but with error log message.
                    Trace.TraceError("Failed to start up SwivlChico component. Service continues to run, but the device is not recognized without restart of the service.");
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
                    Trace.TraceError("Failed to start up Kuando component. Service continues to run without support for the device");
                }
            }
            // TODO: add here for device specific start up when another device type is added.
            else
            {
                throw new InvalidOperationException("Specified device type is not supported: " + Properties.Settings.Default.DeviceType);
            }

            // Start processing of the state machine.
            this.stateMachine.Start(this.remoteRecorderSync, lightControl, resultReceiver);
            Trace.TraceInformation("Started program");
        }

        /// <summary>
        /// Stop handler.
        /// </summary>
        protected override void OnStop()
        {
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