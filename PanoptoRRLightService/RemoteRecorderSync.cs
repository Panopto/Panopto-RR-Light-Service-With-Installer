using Panopto.RemoteRecorderAPI.V1;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.ServiceProcess;
using System.Threading;

namespace RRLightProgram
{
    public class RemoteRecorderSync
    {
        private const string RemoteRecorderServiceName = "Panopto Remote Recorder Service";

        /// <summary>
        /// ServiceController.WaitForStatus() returns before the service has completely started,
        /// so we have to give it a bit more time.
        /// </summary>
        private static readonly TimeSpan RemoteRecorderServiceSetupBreak = TimeSpan.FromSeconds(1.0);

        /// <summary>
        /// Remote Recorder controller.
        /// </summary>
        private IRemoteRecorderController controller;


        private bool shouldStop = false;

        /// <summary>
        /// State machine interface to post events.
        /// </summary>
        private IStateMachine stateMachine;

        //Property to determine whether the current version of the remote recorder supports starting a new recording.
        public bool SupportsStartNewRecording { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="stateMachine">Interface to the state machine.</param>
        /// <exception cref="ApplicationException">Thrown if failing to connect to Remote Recoder</exception>
        public RemoteRecorderSync(IStateMachine stateMachine)
        {
            this.SetUpController();

            this.stateMachine = stateMachine;

            try
            {
                //Try to get the current remote recorder version number
                Process result = Process.GetProcessesByName("RemoteRecorder").FirstOrDefault();
                if (result == null)
                {
                    throw new ApplicationException("Remote recoder process is not running.");
                }
                AssemblyName an = AssemblyName.GetAssemblyName(result.MainModule.FileName);
                if (an == null || an.Version == null)
                {
                    throw new ApplicationException("Remote recoder assembly name is not accessible.");
                }

                this.SupportsStartNewRecording = (an.Version.CompareTo(Version.Parse("5.0")) >= 0);
            }
            catch (Exception e)
            {
                // If we fail to get the RR version, assuming it is 5.0.0 or above.
                Trace.TraceWarning(@"Failed to get remote recoder version. Assuming 5.0.0+. {0}", e);
                this.SupportsStartNewRecording = true;
            }

            //Start background thread to listen for input from recorder
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate { BackgroundPollingWorker(); };
            bgw.RunWorkerAsync();
        }


        // Stop the background thread
        public void Stop()
        {
            this.shouldStop = true;
        }


        #region Public methods to take action against Remote Recorder

        /// <summary>
        /// Stop the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool StopCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState state = this.controller.GetCurrentState();
                if (state.CurrentRecording != null)
                {
                    if (state.Status != RemoteRecorderStatus.Stopped)
                    {
                        this.controller.StopCurrentRecording(state.CurrentRecording.Id);
                        result = true;
                    }
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        ///  Resume the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool ResumeCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState state = this.controller.GetCurrentState();
                if (state.Status != RemoteRecorderStatus.Recording)
                {
                    this.controller.ResumeCurrentRecording(state.CurrentRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        /// Pause the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool PauseCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState state = this.controller.GetCurrentState();
                if (state.Status != RemoteRecorderStatus.Paused)
                {
                    this.controller.PauseCurrentRecording(state.CurrentRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        /// Start the next recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool StartNextRecording()
        {
            bool result = false;

            try
            {
                Recording nextRecording = this.controller.GetNextRecording();
                RemoteRecorderState state = this.controller.GetCurrentState();

                if (state.Status != RemoteRecorderStatus.Recording &&
                    state.CurrentRecording != nextRecording)
                {
                    this.controller.StartNextRecording(nextRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        /// Start a new recording (not a webcast)
        /// </summary>
        /// <returns>true on success</returns>
        public bool StartNewRecording()
        {
            bool result = false;

            try
            {
                Recording nextRecording = this.controller.GetNextRecording();
                RemoteRecorderState state = this.controller.GetCurrentState();

                if (state.Status != RemoteRecorderStatus.Recording &&
                    state.CurrentRecording == null &&
                    nextRecording == null)
                {
                    this.controller.StartNewRecording(false);
                    result = true;
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        /// Extend the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool ExtendCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState state = this.controller.GetCurrentState();
                if (state.CurrentRecording != null)
                {
                    this.controller.ExtendCurrentRecording(state.CurrentRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return result;
        }

        #endregion Public methods to take action against Remote Recorder

        #region Helper methods

        /// <summary>
        /// Creates channel for RR endpoint.
        /// </summary>
        private void SetUpController()
        {
            ChannelFactory<IRemoteRecorderController> channelFactory = new ChannelFactory<IRemoteRecorderController>(
                new NetNamedPipeBinding(),
                new EndpointAddress(Panopto.RemoteRecorderAPI.V1.Constants.ControllerEndpoint));
            
            this.controller = channelFactory.CreateChannel();
        }

        /// <summary>
        /// If RR isn't running, waits until service is running again, then resets the controller.
        /// Otherwise logs the exception from RR and continues.
        /// </summary>
        /// <param name="e">Exception to handle</param>
        /// <param name="blockUntilRunning">True iff this should block current thread until RR service is running</param>
        private void HandleRRException(Exception e, bool blockUntilRunning)
        {
            // EndpointNotFoundException raised if channel is created before RR service is running;
            // FaultException raised if RR service stops after channel is connected to it.
            if (blockUntilRunning && (e is EndpointNotFoundException || e is FaultException))
            {
                using (ServiceController rrController = new ServiceController(RemoteRecorderServiceName))
                {
                    // Wait until RR service has started
                    rrController.WaitForStatus(ServiceControllerStatus.Running);

                    // Note that if this break isn't long enough, we'll hit another
                    // exception and return to this loop, so it's safe.
                    Thread.Sleep(RemoteRecorderSync.RemoteRecorderServiceSetupBreak);

                    SetUpController();
                }
            }
            else
            {
                // Log and continue; problem could be temporary.
                Trace.TraceError("Error calling remote recorder process: {0}", e);
            }
        }

        #endregion Helper methods

        #region State monitor

        /// <summary>
        ///  Runs on a background thread to monitor the remoterecorder state and will dispatch events back to the
        ///  main thread when the state changes.
        /// </summary>
        private void BackgroundPollingWorker()
        {
            Input previousStateAsInput = MapInputFrom(RemoteRecorderStatus.Disconnected);

            while (!this.shouldStop)
            {
                Exception exceptionInRR = null;
                Input stateAsInput;
                try
                {
                    // Get the current state from the RR process
                    stateAsInput = MapInputFrom(this.controller.GetCurrentState().Status);
                }
                catch (Exception e)
                {
                    // If there's a problem with the RR, consider it disconnected and update the state machine.
                    stateAsInput = MapInputFrom(RemoteRecorderStatus.Disconnected);
                    exceptionInRR = e;
                }

                // If state changed, post the new state to the state machine.
                if (stateAsInput != previousStateAsInput)
                {
                    if (stateAsInput != Input.NoInput)
                    {
                        this.stateMachine.PostInput(stateAsInput);
                    }
                    previousStateAsInput = stateAsInput;
                }

                // Handle exception after the state machine has been updated
                if (exceptionInRR != null)
                {
                    // Blocks while RR service is not running.
                    HandleRRException(exceptionInRR, true);
                    exceptionInRR = null;
                }

                // Sleep for a moment before polling again to avoid spinlock
                Thread.Sleep(RRLightProgram.Properties.Settings.Default.RecorderPollingInterval);
            }
        }

        /// <summary>
        /// Map the remote recorder status to the input event type of the state machine.
        /// </summary>
        private Input MapInputFrom(RemoteRecorderStatus state)
        {
            switch (state)
            {
                case RemoteRecorderStatus.Stopped:
                    return Input.RecorderStopped;

                case RemoteRecorderStatus.Recording:
                    return Input.RecorderRecording;

                case RemoteRecorderStatus.RecorderRunning:
                    return Input.RecorderRunning;

                case RemoteRecorderStatus.Previewing:
                    Recording nextRecording = controller.GetNextRecording();
                    if (nextRecording != null)
                    {
                        return Input.RecorderPreviewingQueued;
                    }
                    else
                    {
                        return Input.RecorderPreviewing;
                    }

                case RemoteRecorderStatus.Paused:
                    return Input.RecorderPaused;

                case RemoteRecorderStatus.Faulted:
                    return Input.RecorderFaulted;

                case RemoteRecorderStatus.Disconnected:
                    return Input.Disconnected;

                default:
                    return Input.NoInput;
            }
        }

        #endregion State monitor
    }
}