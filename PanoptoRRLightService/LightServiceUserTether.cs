using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using System.ServiceModel;
using Panopto.RemoteRecorderAPI.V1;
using System.Threading;
using APIRecording = Panopto.RemoteRecorderAPI.V1.Recording;

namespace RRLightProgram
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class LightServiceTether : IWindowsRecorderUserSessionTether, IRemoteRecorderController
    {
        private ConcurrentQueue<RecorderActionInfo> actionQueue = new ConcurrentQueue<RecorderActionInfo>();
        private List<RecorderActionResult> resultList = new List<RecorderActionResult>();
        private ManualResetEvent resultAvailableEvent = new ManualResetEvent(false);
        private RemoteRecorderState windowsRecorderState = null;
        ServiceHost TetherHost;

        public void SetupUserTether()
        {
            this.TetherHost = new ServiceHost(this);
            this.TetherHost.AddServiceEndpoint(typeof(IWindowsRecorderUserSessionTether),
                new NetNamedPipeBinding(),
                Properties.Settings.Default.UserControlEndpoint);

            this.TetherHost.Open();
        }

        public void StopUserTether()
        {
            this.TetherHost.Close();
            this.TetherHost = null;
        }

        /// <summary>
        /// UserSessionProxy will report the state of the recorder to the Tether
        /// </summary>
        /// <param name="state"></param>
        public void ReportState(RemoteRecorderState state)
        {
            windowsRecorderState = state;
        }

        /// <summary>
        /// UserSessionProxy will call this method with the result of the executed command
        /// </summary>
        /// <param name="result"></param>
        public void ReportActionResult(RecorderActionResult result)
        {
            lock (resultList)
            {
                // This adds to the back of the list (important for GetCommandResult logic)
                resultList.Add(result);
            }

            // Setting the MRE will wake up GetCommandResult() if it's waiting
            resultAvailableEvent.Set();
        }

        /// <summary>
        /// Return the result of the requested command, identified by commandId
        /// </summary>
        /// <param name="commandId"></param>
        /// <returns></returns>
        public RecorderActionResult GetCommandResult(Guid commandId)
        {
            while(true)
            {
                // Wait up to 2.5s for ReportActionResult() to set the ManualResetEvent
                // Note: It's important to check if WaitOne was triggered here so that another thread can't reset the MRE
                // before we get a chance to see if the MRE was triggered or timed out
                bool wasThreadAwoken = resultAvailableEvent.WaitOne(2500);

                lock (resultList)
                {
                    // Reset the MRE in case ReportActionResult() is called while we are checking the results
                    resultAvailableEvent.Reset();
                    for (int i = 0; i < resultList.Count; i++)
                    {
                        if (resultList[i].CommandId == commandId)
                        {
                            RecorderActionResult result = resultList[i];
                            resultList.RemoveAt(i);
                            return result;
                        }
                        else if (resultList[i].ResultTimestamp.AddMinutes(
                            Properties.Settings.Default.StaleTetherMessageDiscardTime.TotalMinutes) < DateTime.UtcNow)
                        {
                            Trace.TraceInformation("Throwing away result id: " + resultList[i].CommandId + " since it was older than 1 minute");
                            resultList.RemoveAt(i);
                        }
                    }
                }

                // The ManualResetEvent timed out and we didn't find the result, return fallback result
                if (!wasThreadAwoken)
                {
                    Trace.TraceWarning("Did not get result from Windows Recorder, assuming failure for now");
                    return (new RecorderActionResult() { BoolResult = false, ApiResult = null, VersionResult = null });
                }

                // The MRE was set, but we didn't find the result, which means that we were woken up by another thread,
                // go back to beginning of loop and try again
            }
        }

        /// <summary>
        /// If the queue is empty, return an empty tuple
        /// </summary>
        /// <returns></returns>
        public RecorderActionInfo GetQueuedCommand()
        {
            RecorderActionInfo command = null;
            actionQueue.TryDequeue(out command);
            
            return command;
        }

        /// <summary>
        /// Primary way for user session to check to see if it's connected to tether
        /// </summary>
        /// <returns></returns>
        public bool IsTetherRunning()
        {
            return this.TetherHost != null;
        }

        #region IRemoteRecorderController Implementation

        public bool StartNextRecording(Guid id)
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.StartNext, CommandId = commandId });

            return GetCommandResult(commandId).BoolResult;
        }

        public bool StartNewRecording(bool isWebcast)
        {
            Guid commandId = Guid.NewGuid();
            if (isWebcast)
            {
                actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.StartWebcast, CommandId = commandId });
            }
            else
            {
                actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.Start, CommandId = commandId });
            }

            return GetCommandResult(commandId).BoolResult;
        }

        public bool PauseCurrentRecording(Guid id)
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.Pause, SessionId = id, CommandId = commandId });

            return GetCommandResult(commandId).BoolResult;
        }

        public bool ResumeCurrentRecording(Guid id)
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.Resume, SessionId = id, CommandId = commandId });

            return GetCommandResult(commandId).BoolResult;
        }

        public bool StopCurrentRecording(Guid id)
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.Stop, SessionId = id, CommandId = commandId });

            return GetCommandResult(commandId).BoolResult;
        }

        public bool StopAndDeleteCurrentRecording(Guid id)
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.StopDelete, SessionId = id, CommandId = commandId });

            return GetCommandResult(commandId).BoolResult;
        }

        public bool ExtendCurrentRecording(Guid id)
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.Extend, CommandId = commandId });

            return GetCommandResult(commandId).BoolResult;
        }

        public RemoteRecorderState GetCurrentState()
        {
            return windowsRecorderState;
        }

        public APIRecording GetNextRecording()
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.GetNext, CommandId = commandId });

            return GetCommandResult(commandId).ApiResult;
        }

        // GetRecorderVersion is never called for the Windows Recorder
        public Version GetRecorderVersion()
        {
            Guid commandId = Guid.NewGuid();
            actionQueue.Enqueue(new RecorderActionInfo() { Action = RecorderAction.Version, CommandId = commandId });

            return GetCommandResult(commandId).VersionResult;
        }
    }

    #endregion IRemoteRecorderController Implementation
}
