using Panopto.RemoteRecorderAPI.V1;
using System;
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
        #region Variables

        private const string RemoteRecorderServiceName = "Panopto Remote Recorder Service";
        private const string RemoteRecorderProcessName = "RemoteRecorder";

        /// <summary>
        /// Remote Recorder controller.
        /// </summary>
        private IRemoteRecorderController controller;

        /// <summary>
        /// State machine interface to post events.
        /// </summary>
        private IStateMachine stateMachine;

        /// <summary>
        /// Background thread to monitor the remote recorder state.
        /// </summary>
        private Thread stateMonitorThread;

        /// <summary>
        /// Stop request for the background thread.
        /// </summary>
        private ManualResetEvent stateMonitorThreadToStop;

        /// <summary>
        /// Property to determine whether the current version of the remote recorder supports starting a new recording.
        /// </summary>
        public bool SupportsStartNewRecording { get; private set; }

        #endregion Variables

        #region Initialization and Cleanup

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="stateMachine">Interface to the state machine.</param>
        /// <exception cref="TimeoutException">Thrown if failing to connect to Remote Recoder. Service may not run.</exception>
        public RemoteRecorderSync(IStateMachine stateMachine)
        {
            // This waits for RR to start.
            this.SetUpController();

            this.stateMachine = stateMachine;

            // Get the current remote recorder version number
            // This should succeed because SetUpController has confirmed the service is up.
            Process process = Process.GetProcessesByName(RemoteRecorderSync.RemoteRecorderProcessName).FirstOrDefault();
            if (process == null)
            {
                throw new ApplicationException("Remote recorder process is not running.");
            }
            
            try
            {
                AssemblyName an = AssemblyName.GetAssemblyName(process.MainModule.FileName);
                this.SupportsStartNewRecording = (an.Version.CompareTo(Version.Parse("5.0")) >= 0);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // This may happen if this service runs without admin privilege.
                Trace.TraceInformation("Assembly information of Remote Recorder is not available. Assuming 5.0.0+.");
                this.SupportsStartNewRecording = true;
            }

            // Start background thread to monitor the recorder state.
            this.stateMonitorThread = new Thread(StateMonitorLoop);
            this.stateMonitorThreadToStop = new ManualResetEvent(initialState: false);
            this.stateMonitorThread.Start();
        }

        // Stop the background thread
        public void Stop()
        {
            this.stateMonitorThreadToStop.Set();
            this.stateMonitorThread.Join();
        }

        #endregion Initialization and Cleanup

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
                        result = this.controller.StopCurrentRecording(state.CurrentRecording.Id);
                        if (!result)
                        {
                            Trace.TraceWarning("StopCurrentRecording failed.");
                        }
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
        /// Stop and delete the current recording (preventing upload)
        /// </summary>
        /// <returns>true on success</returns>
        public bool StopAndDeleteCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState state = this.controller.GetCurrentState();
                if (   state.CurrentRecording != null
                    && state.Status != RemoteRecorderStatus.Stopped)
                {
                    result = this.controller.StopAndDeleteCurrentRecording(state.CurrentRecording.Id);

                    if (!result)
                    {
                        Trace.TraceWarning("StopAndDeleteCurrentRecording failed.");
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
                    result = this.controller.ResumeCurrentRecording(state.CurrentRecording.Id);
                    if (!result)
                    {
                        Trace.TraceWarning("ResumeCurrentRecording failed.");
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
                    result = this.controller.PauseCurrentRecording(state.CurrentRecording.Id);
                    if (!result)
                    {
                        Trace.TraceInformation("PauseCurrentRecording failed. This is expected if the recording is webcast.");
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
                    result = this.controller.StartNextRecording(nextRecording.Id);
                    if (!result)
                    {
                        Trace.TraceWarning("StartNextRecording failed.");
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
                    result = this.controller.StartNewRecording(false);

                    if (!result)
                    {
                        Trace.TraceWarning("StartNewRecording failed.");
                    }
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
        /// Wait for the remote recorder service to start and creates channel for RR endpoint.
        /// </summary>
        private void SetUpController()
        {
            // Wait until RR service has started. Message every minute while waiting.
            while (true)
            {
                try
                {
                    using (ServiceController serviceController = new ServiceController(RemoteRecorderServiceName))
                    {
                        serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60.0));
                        break;
                    }
                }
                catch (System.TimeoutException)
                {
                    Trace.TraceInformation("RemoteRecorderSync: Waiting for the recorder to start up.");
                }
                catch (InvalidOperationException)
                {
                    // InvalidOperationException is not documented, but it is actually thrown immedately after the system boot.
                    Trace.TraceInformation("RemoteRecorderSync: Service controller is not yet unavailable. Retry after 60 seconds.");
                    Thread.Sleep(TimeSpan.FromSeconds(60.0));
                }
            }

            // ServiceController.WaitForStatus() may return before the service has completely started,
            // so we have to give it a bit more time. Note that if this break isn't long enough,
            // we'll hit an exception later and return to HandleRRException again, so it's safe.
            Thread.Sleep(TimeSpan.FromSeconds(1.0));

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
            // FaultException raised if RR service stops after channel is connected to it;
            // CommunicationException is raised after the channel is broken with some reason.
            if (blockUntilRunning && (e is EndpointNotFoundException || e is FaultException || e is CommunicationException))
            {
                Trace.TraceWarning("Error calling remote recorder process. Reconnecting: {0}", e);
                SetUpController();
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
        ///  Runs on a background thread to monitor the remote recorder state and will dispatch events to the state machine.
        /// </summary>
        private void StateMonitorLoop()
        {
            Input previousStateAsInput = MapInputFrom(RemoteRecorderStatus.Disconnected);

            // Loop with sleep (by the timeout of waiting for stop request)
            do
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
                    if (stateAsInput != Input.None)
                    {
                        this.stateMachine.PostInput(stateAsInput);
                    }
                    previousStateAsInput = stateAsInput;
                }

                // Handle exception after the state machine has been updated
                if (exceptionInRR != null)
                {
                    // Blocks while RR service is not running.
                    HandleRRException(exceptionInRR, blockUntilRunning: true);
                    exceptionInRR = null;
                }
            } while (!this.stateMonitorThreadToStop.WaitOne(Properties.Settings.Default.RecorderPollingInterval));
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
                    if (Properties.Settings.Default.RequireOptInForRecording)
                    {
                        //If the settings require opt-in, then go to the 'potential recording' state
                        return Input.RecorderStartedPotentialRecording;
                    }
                    else
                    {
                        return Input.RecorderRecording;
                    }

                case RemoteRecorderStatus.RecorderRunning:
                    return Input.RecorderDormant;

                case RemoteRecorderStatus.Previewing:
                    Recording nextRecording = controller.GetNextRecording();
                    if (nextRecording != null)
                    {
                        return Input.RecorderPreviewingWithNextSchedule;
                    }
                    else
                    {
                        return Input.RecorderPreviewingNoNextSchedule;
                    }

                case RemoteRecorderStatus.Paused:
                    return Input.RecorderPaused;

                case RemoteRecorderStatus.Faulted:
                    return Input.RecorderFaulted;

                case RemoteRecorderStatus.Disconnected:
                    return Input.RecorderDisconnected;

                default:
                    return Input.None;
            }
        }

        #endregion State monitor
    }
}