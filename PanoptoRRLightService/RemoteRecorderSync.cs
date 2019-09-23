using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.ServiceProcess;
using System.Threading;

using Panopto.RemoteRecorderAPI.V1;

namespace RRLightProgram
{
    public class RemoteRecorderSync
    {
        #region Variables

        /// <summary>
        /// Service name of Remote Recorder service. This is not Display Name, i.e. unique regardless of langauges.
        /// </summary>
        private const string RemoteRecorderServiceName = "PanoptoRemoteRecorderService";

        /// <summary>
        /// Process name of Remote Recorder service.
        /// </summary>
        private const string RemoteRecorderProcessName = "RemoteRecorder";

        /// <summary>
        /// Process name of Windows Recorder application
        /// </summary>
        private const string WindowsRecorderProcessName = "Recorder";

        /// <summary>
        /// The sleep time (in seconds) for each loop of looking for windows recorder
        /// </summary>
        private readonly int RecorderInterval = Properties.Settings.Default.CheckIntervalForRecorder;

        /// <summary>
        /// Count the number of times the program has waited, so that we dont log spam
        /// </summary> 
        public int LogWaitingCount = 0;

        /// <summary>
        /// Running count of hours waited
        /// </summary>
        private int HoursWaited = 0;

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
        /// Controls the connection to the user RRLightService process
        /// </summary>
        private LightServiceTether windowsTether;

        public Boolean isRemoteRecorder;

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
        public RemoteRecorderSync(IStateMachine stateMachine, LightServiceTether windowsTether)
        {
            // Connect to the WR or RR endpoint
            this.windowsTether = windowsTether;
            this.SetUpController();

            this.stateMachine = stateMachine;

            // Get the current remote recorder version number
            // This should succeed because SetUpController has confirmed the service is up.
            Process process = Process.GetProcessesByName(RemoteRecorderSync.RemoteRecorderProcessName).FirstOrDefault();

            // Check if the remote recorder exists on the computer
            // If it does set it up, else skip and start the state machine
            if (process != null)
            {
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
        public void HandOffToWR()
        {
            isRemoteRecorder = false;
            this.controller = this.windowsTether;
        }

        /// <summary>
        /// Set controller to null, StateMonitorLoop() will kick off SetupController when
        /// it detects controller is null
        /// </summary>
        public void ResetController()
        {
            this.controller = null;
        }

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
                RemoteRecorderState state = this.controller.GetCurrentState();

                if (state.Status != RemoteRecorderStatus.Recording &&
                    state.CurrentRecording == null)
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
                    if (state.Status != RemoteRecorderStatus.Stopped)
                    {
                        result = this.controller.ExtendCurrentRecording(state.CurrentRecording.Id);
                        if (!result)
                        {
                            Trace.TraceWarning("ExtendCurrentRecording failed.");
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
        /// Get current recording data
        /// </summary>
        /// <returns>Recording object</returns>
        public Recording GetCurrentRecording()
        {
            Recording recordingData = null;
            try
            {
                recordingData = this.controller.GetCurrentState().CurrentRecording;

            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return recordingData;
        }

        /// <summary>
        /// Gets the next recording within the range of both the controller GetNextRecording call
        /// and the locally defined limit as per the GetNextRecordingTimeLimitOverride config
        /// </summary>
        /// <returns>The upcoming recording or null if no recording is in range</returns>
        public Recording GetNextRecordingWithinRange()
        {
            Recording recordingData = null;
            try
            {
                recordingData = this.controller.GetNextRecording();
                if (recordingData?.StartTime > DateTime.UtcNow + Properties.Settings.Default.GetNextRecordingTimeLimitOverride)
                {
                    recordingData = null;
                }
            }
            catch (Exception e)
            {
                this.HandleRRException(e, false);
            }

            return recordingData;
        }

        #endregion Public methods to take action against Remote Recorder

        #region Helper methods

        /// <summary>
        /// Returns true if the Windows Recorder is found
        /// </summary>
        /// <returns></returns>
        public bool LookForWR()
        {
            Process[] process = Process.GetProcessesByName(RemoteRecorderSync.WindowsRecorderProcessName);

            // Background processes have a SessionId of 0, so make sure it isn't a background process
            if (process != null)
            {
                foreach (Process proc in process)
                {
                    if (proc.SessionId != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Return true if the Remote Recorder is found and running
        /// </summary>
        /// <returns></returns>
        public bool LookForRR()
        {
            try
            {
                using (ServiceController serviceController = new ServiceController(RemoteRecorderServiceName))
                {
                    if (serviceController.Status == ServiceControllerStatus.Running)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // RR is not found
                return false;
            }
        }

        /// <summary>
        /// Wait for the remote recorder service or windows recorder app to start and creates channel for endpoint.
        /// If the RR and WR are running, the RR will initially connect, the code will detect it's in a dormant state
        /// and it will handoff to the WR
        /// </summary>
        private void SetUpController()
        {
            // Hour calculator: (60 / seconds) * 60 -> simplified to: (3600 / seconds)
            int hour = (3600 / RecorderInterval);

            // We want to log the first message, so set it to the hour point to start
            LogWaitingCount = hour;

            // Wait until RR or WR service has started. Message every hour.
            while (true)
            {
                if (LookForRR())
                {
                    isRemoteRecorder = true;
                    break;
                }
                if (LookForWR())
                {
                    isRemoteRecorder = false;
                    break;
                }
                Thread.Sleep(TimeSpan.FromSeconds(RecorderInterval));

                // Print the log every hour, to avoid log spam
                if (LogWaitingCount >= hour)
                {
                    Trace.TraceInformation("RemoteRecorderSync: RR and WR not found. Retrying every {0} seconds. " +
                        "Have been looking for {1} hour(s)", RecorderInterval, HoursWaited);
                    LogWaitingCount = 0;
                    HoursWaited++;
                }
                LogWaitingCount++;
            }

            HoursWaited = 0;

            if (isRemoteRecorder)
            {
                ChannelFactory<IRemoteRecorderController> channelFactory = new ChannelFactory<IRemoteRecorderController>(
                    new NetNamedPipeBinding(),
                    new EndpointAddress(Constants.ControllerEndpoint));
                this.controller = channelFactory.CreateChannel();
            }
            else
            {
                this.controller = this.windowsTether;
            }
        }

        /// <summary>
        /// If RR isn't running, waits until service is running again, then resets the controller.
        /// Otherwise logs the exception from RR and continues.
        /// </summary>
        /// <param name="e">Exception to handle</param>
        /// <param name="blockUntilRunning">True iff this should block current thread until RR service is running</param>
        private void HandleRRException(Exception e, bool blockUntilRunning)
        {
            // EndpointNotFoundException raised if recorder is open but net.pipe communication is failing;
            // FaultException raised if RR service stops after channel is connected to it;
            // CommunicationException is raised after the channel is broken with some reason.
            if (blockUntilRunning && (e is EndpointNotFoundException || e is FaultException || e is CommunicationException))
            {
                Trace.TraceWarning("Error calling remote or windows recorder process. Reconnecting to either recorder: {0}", e);
                SetUpController();
            }
            else
            {
                // Log and continue; problem could be temporary.
                Trace.TraceError("Error calling remote or windows recorder process: {0}", e);
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
                // Reset the controller if it's null
                if (this.controller == null)
                {
                    SetUpController();
                }

                Exception exceptionInRR = null;
                Input stateAsInput;

                try
                {
                    // Get the current state from the RR or WR process
                    stateAsInput = MapInputFrom(this.controller.GetCurrentState().Status);

                    if (stateAsInput == Input.RecorderRecording)
                    {
                        // If reamining time is within the window of ending warning, chagne the state to RecordingRecordingFinishWarningTriggered
                        TimeSpan remainingTime = GetCurrentRecording().EndTime - DateTime.UtcNow;
                        if (remainingTime < Properties.Settings.Default.RecordEndingWindowStart &&
                            remainingTime > TimeSpan.Zero)
                        {
                            stateAsInput = Input.RecorderRecordingEnteredEndingWindow;
                        }
                    }
                    
                }
                catch (Exception e)
                {
                    // If there's a problem with the recorder, consider it disconnected and update the state machine.
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
                    // Blocks while recorder service is not running.
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
            Recording nextRecording;
            switch (state)
            {
                case RemoteRecorderStatus.Stopped:
                    nextRecording = this.GetNextRecordingWithinRange();
                    if (nextRecording != null)
                    {
                        return Input.RecorderStoppedWithNextSchedule;
                    }
                    else
                    {
                        return Input.RecorderStoppedNoNextSchedule;
                    }

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
                    nextRecording = this.GetNextRecordingWithinRange();
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