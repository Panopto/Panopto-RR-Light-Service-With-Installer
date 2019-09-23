using System;
using System.ServiceModel;
using System.Threading;
using System.Diagnostics;
using Panopto.RemoteRecorderAPI.V1;
using APIRecording = Panopto.RemoteRecorderAPI.V1.Recording;
using System.Collections.Generic;

namespace RRLightProgram
{
    class UserSessionProxy
    {
        private IRemoteRecorderController windowsRecorderController;
        private IWindowsRecorderUserSessionTether masterController;
        private EventWaitHandle ShutdownSignal;
        private readonly TimeSpan masterPingRate = TimeSpan.Parse("00:00:01");

        public UserSessionProxy()
        {
            ShutdownSignal = new EventWaitHandle(false, EventResetMode.ManualReset);
            Microsoft.Win32.SystemEvents.SessionEnding += new Microsoft.Win32.SessionEndingEventHandler(OnShutdown);
        }

        // Flow of Run()
        // Every second (masterPingRate), the user session has 2 'jobs':
        //      Try to get any user requested actions from the master service
        //      Try to report the windows recorder state to the master service
        // Run() will catch any errors uncaught by these 'jobs' but it will assume that any uncaught errors
        // are from an inability to communicate to the master process.
        // It is up to the 2 'jobs' to catch any errors communicating with the windows recorder
        public void Run()
        {
            WaitForMasterProcess();
            WaitForWindowsRecorder();

            while (!this.ShutdownSignal.WaitOne(this.masterPingRate))
            {
                try
                {
                    if (!IsRecorderOpen())
                    {
                        WaitForWindowsRecorder();
                    }

                    ActOnNextQueue();
                    ReportRecorderState(GetRecorderState());
                }
                catch
                {
                    // Make sure Master process is still alive & running
                    WaitForMasterProcess();
                }
            }
        }

        /// <summary>
        /// OnShutdown gets called when the process receives a stop command
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnShutdown(object sender, EventArgs e)
        {
            ShutdownSignal.Set();
        }

        /// <summary>
        /// Open the connection to the master process
        /// </summary>
        public void SetupMasterController()
        {
            ChannelFactory<IWindowsRecorderUserSessionTether> channelFactory = new ChannelFactory<IWindowsRecorderUserSessionTether>(
            new NetNamedPipeBinding(),
            new EndpointAddress(Properties.Settings.Default.UserControlEndpoint));

            this.masterController = channelFactory.CreateChannel();
        }

        /// <summary>
        /// Open the connection to the windows recorder
        /// </summary>
        public void SetupWindowsController()
        {
            ChannelFactory<IRemoteRecorderController> channelFactory = new ChannelFactory<IRemoteRecorderController>(
                new NetNamedPipeBinding(),
                new EndpointAddress(Constants.WindowsRecorderControllerEndpoint));
            this.windowsRecorderController = channelFactory.CreateChannel();
        }

        private void WaitForMasterProcess()
        {
            SetupMasterController();
            Trace.TraceInformation("Looking for Master RRLightService service");
            while (!this.ShutdownSignal.WaitOne(this.masterPingRate))
            {
                try
                {
                    if (this.masterController.IsTetherRunning())
                    {
                        Trace.TraceInformation("Master RRLightService found and running");
                        break;
                    }
                }
                catch
                {
                    // Tether not found, continue waiting...
                    SetupMasterController();
                }
            }
        }

        private void WaitForWindowsRecorder()
        {
            Trace.TraceInformation("Looking for Windows Recorder");
            while (!this.ShutdownSignal.WaitOne(this.masterPingRate))
            {
                if (IsRecorderOpen())
                {
                    SetupWindowsController();
                    Trace.TraceInformation("Windows Recorder found and connected");
                    break;
                };
            }
        }

        private bool IsRecorderOpen()
        {
            Process[] process = Process.GetProcessesByName("Recorder");

            // Background processes have a SessionId of 0, so make sure it isn't a background process
            if (process != null)
            {
                for (int i = 0; i < process.Length; i++)
                {
                    if (process[i].SessionId != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Report to the master service the state of the windows recorder
        /// </summary>
        private RemoteRecorderState GetRecorderState()
        {
            RemoteRecorderState recorderState;
            try
            {
                recorderState = this.windowsRecorderController.GetCurrentState();
            }
            catch
            {
                // If something is wrong with the windows recorder, report that it is disconnected
                recorderState = new RemoteRecorderState() { Status = RemoteRecorderStatus.Disconnected };

                // Need to setup windows controller again since the connection is now in a disconnected state
                SetupWindowsController();
            }

            // If something fails in this call the Run() try/catch will catch the error
            return recorderState;
        }

        /// <summary>
        /// Report to the master service a specified state
        /// </summary>
        /// <param name="state"></param>
        private void ReportRecorderState(RemoteRecorderState state)
        {
            this.masterController.ReportState(state);
        }

        /// <summary>
        /// The master service will queue all user commands requested
        /// This goes through the queue and executes requested commands onto the Windows Recorder
        /// </summary>
        private void ActOnNextQueue()
        {
            RecorderActionInfo info = this.masterController.GetQueuedCommand();

            if (info == null)
            {
                return;
            }

            try
            {
                // 3 result variables
                bool result;
                Version versionResult;
                APIRecording apiResult;

                switch (info.Action)
                {
                    case (RecorderAction.None):
                        break;
                    case (RecorderAction.Pause):
                        result = this.windowsRecorderController.PauseCurrentRecording(info.SessionId);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.Resume):
                        result = this.windowsRecorderController.ResumeCurrentRecording(info.SessionId);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.Start):
                        result = this.windowsRecorderController.StartNewRecording(false);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.StartWebcast):
                        result = this.windowsRecorderController.StartNewRecording(true);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.Stop):
                        result = this.windowsRecorderController.StopCurrentRecording(info.SessionId);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.StopDelete):
                        result = this.windowsRecorderController.StopAndDeleteCurrentRecording(info.SessionId);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.Extend):
                        result = this.windowsRecorderController.ExtendCurrentRecording(info.SessionId);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                    case (RecorderAction.GetNext):
                        apiResult = this.windowsRecorderController.GetNextRecording();
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, ApiResult = apiResult });
                        break;
                    case (RecorderAction.Version):
                        versionResult = this.windowsRecorderController.GetRecorderVersion();
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, VersionResult = versionResult });
                        break;
                    case (RecorderAction.StartNext):
                        result = this.windowsRecorderController.StartNextRecording(info.SessionId);
                        this.masterController.ReportActionResult(
                            new RecorderActionResult() { CommandId = info.CommandId, BoolResult = result });
                        break;
                }
            }
            catch
            {
                // Assume something went wrong with talking to the windows controller
                // Their is potential that the masterController threw the exception, but that will be handled in Run()
                Trace.TraceWarning("Was unable to communicate with the Windows Recorder when expected");
                ReportRecorderState(new RemoteRecorderState() { Status = RemoteRecorderStatus.Disconnected });

                // Need to setup windows controller again since the connection is now in a disconnected state
                SetupWindowsController();
            }
        }
    }
}
