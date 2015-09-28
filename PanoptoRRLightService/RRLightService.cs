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
        private static bool SELF_SIGNED = true; // Target server is a self-signed server
        private static bool initialized = false;
        private static MainAppLogic mal;
        private Thread MainThread;

        public RRLightService()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Manually run the service to support console debugging
        /// </summary>
        public void ManualRun()
        {
            this.OnStart(null);
        }

        protected override void OnStart(string[] args)
        {
            if (SELF_SIGNED)
            {
                // For self-signed servers
                EnsureCertificateValidation();
            }

            // TODO: Parse args if necessary

            mal = new MainAppLogic();
            MainThread = new Thread(new ThreadStart(mal.Main));
            MainThread.Start();
        }

        protected override void OnStop()
        {
            mal.Stop();
            MainThread.Abort();
        }

        /// <summary>
        /// Ensures that our custom certificate validation has been applied
        /// </summary>
        public static void EnsureCertificateValidation()
        {
            if (!initialized)
            {
                ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(CustomCertificateValidation);
                initialized = true;
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

    public class MainAppLogic
    {
        //Boolean used for stopping thread loop
        private bool shouldStop = false;

        //Initialize queue for events to input into statemachine
        private Queue<StateMachine.StateMachineInputArgs> stateMachineInputQueue = new Queue<StateMachine.StateMachineInputArgs>();

        //Initialize threshold for button hold from settings to pass into light.


        // Delegate for the light and the RR to callback to add statemachine input to the queue (in a threadsafe manner)
        public delegate void EnqueueStateMachineInput(StateMachine.StateMachineInputArgs input);

        /// <summary>
        ///     Program main loop
        /// </summary>
        public void Main()
        {
            //Create new DelcomLight object and start it's thread to listen for input from the button
            DelcomLight dLight = new DelcomLight(new EnqueueStateMachineInput(this.AddInputToStateMachineQueue),
                                       RRLightProgram.Properties.Settings.Default.HoldDuration);

            //Create new remote recorder sync object to poll recorder state and input changes into state machine
            RemoteRecorderSync rSync = new RemoteRecorderSync(new EnqueueStateMachineInput(this.AddInputToStateMachineQueue));

            //Initialize state machine. Pass in Light and RemoteRecorder
            StateMachine sm = new StateMachine(dLight, rSync);

            // Main thread loop
            // Loop endlessly until we're asked to stop
            while (!this.shouldStop)
            {
                StateMachine.StateMachineInputArgs argsToProcess = null;

                // lock only while we're inspecting and changing the queue
                lock (stateMachineInputQueue)
                {
                    // if the queue has anything, then work on it
                    if (stateMachineInputQueue.Any())
                    {
                        // dequeue
                        argsToProcess = stateMachineInputQueue.Dequeue();
                    }
                }

                if (argsToProcess != null)
                {
                    if (Program.RunFromConsole)
                    {
                        Trace.TraceInformation(DateTime.Now + ": Processing input: ");
                        Trace.TraceInformation(DateTime.Now + ": " + argsToProcess.Input.ToString() + " " +
                                               DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                        Trace.Flush();
                    }

                    // send the input to the state machine
                    sm.ProcessStateMachineInput(argsToProcess);
                }
                else
                {
                    // else sleep
                    Thread.Sleep(50);
                }
            }
        }

        private void AddInputToStateMachineQueue(StateMachine.StateMachineInputArgs input)
        {
            if (Program.RunFromConsole)
            {
                Trace.TraceInformation(DateTime.Now + ": Detected input: ");
                Trace.TraceInformation(DateTime.Now + ": " + input.Input.ToString() + " " + DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                Trace.Flush();
            }

            lock (stateMachineInputQueue)
            {
                stateMachineInputQueue.Enqueue(input);
            }
        }

        public void Stop()
        {
            shouldStop = true;
        }
    }
}