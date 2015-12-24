﻿using Panopto.RemoteRecorderAPI.V1;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceModel;
using System.ServiceProcess;
using System.Threading;

namespace RRLightProgram
{
    public delegate void RemoteRecorderEventHandler(object sender, StateMachine.StateMachineInputArgs e);

    public class RemoteRecorderSync
    {
        private const string RRServiceName = "Panopto Remote Recorder Service";
        private const int RRServiceSetupInterval = 1000;

        private IRemoteRecorderController controller;
        private bool shouldStop = false;
        private MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback;

        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="stateMachineInputCallback"></param>
        public RemoteRecorderSync(MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback)
        {
            SetUpController();

            this.stateMachineInputCallback = stateMachineInputCallback;

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

        /// <summary>
        ///     Stop the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool StopCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState cState = controller.GetCurrentState();
                if (cState.CurrentRecording != null)
                {
                    if (cState.Status != RemoteRecorderStatus.Stopped)
                    {
                        controller.StopCurrentRecording(cState.CurrentRecording.Id);
                        result = true;
                    }
                }
            }
            catch (Exception e)
            {
                HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        ///     Resume the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool ResumeCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState cState = controller.GetCurrentState();
                if (cState.Status != RemoteRecorderStatus.Recording)
                {
                    controller.ResumeCurrentRecording(cState.CurrentRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        ///     Pause the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool PauseCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState cState = controller.GetCurrentState();
                if (cState.Status != RemoteRecorderStatus.Paused)
                {
                    controller.PauseCurrentRecording(cState.CurrentRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        ///     Start the next recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool StartNextRecording()
        {
            bool result = false;

            try
            {
                Recording nextRecording = controller.GetNextRecording();
                RemoteRecorderState cState = controller.GetCurrentState();

                if (cState.Status != RemoteRecorderStatus.Recording
                    && cState.CurrentRecording != nextRecording)
                {
                    controller.StartNextRecording(nextRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        ///     Start a new recording (not a webcast)
        /// </summary>
        /// <returns>true on success</returns>
        public bool StartNewRecording()
        {
            bool result = false;

            try
            {
                Recording nextRecording = controller.GetNextRecording();
                RemoteRecorderState cState = controller.GetCurrentState();

                if (cState.Status != RemoteRecorderStatus.Recording
                    && cState.CurrentRecording == null
                    && nextRecording == null)
                {
                    controller.StartNewRecording(false);
                    result = true;
                }
            }
            catch (Exception e)
            {
                HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        ///     Extend the current recording
        /// </summary>
        /// <returns>true on success</returns>
        public bool ExtendCurrentRecording()
        {
            bool result = false;

            try
            {
                RemoteRecorderState cState = controller.GetCurrentState();
                if (cState.CurrentRecording != null)
                {
                    controller.ExtendCurrentRecording(cState.CurrentRecording.Id);
                    result = true;
                }
            }
            catch (Exception e)
            {
                HandleRRException(e, false);
            }

            return result;
        }

        /// <summary>
        /// Creates channel for RR endpoint
        /// </summary>
        private void SetUpController()
        {
            ChannelFactory<IRemoteRecorderController> channelFactory = new ChannelFactory<IRemoteRecorderController>(
                new NetNamedPipeBinding(),
                new EndpointAddress(Constants.ControllerEndpoint));
            this.controller = channelFactory.CreateChannel();
        }

        /// <summary>
        /// If RR isn't running, waits until service is running again, then resets the controller. Otherwise
        /// simply logs the RR error and continues.
        /// </summary>
        /// <param name="e">Exception to handle</param>
        /// <param name="blockUntilRunning">True iff this should block current thread until RR service is running</param>
        private void HandleRRException(Exception e, bool blockUntilRunning)
        {
            // EndpointNotFoundException raised if channel is created before RR service is running;
            // FaultException raised if RR service stops after channel is connected to it.
            if (blockUntilRunning && (e is EndpointNotFoundException || e is FaultException))
            {
                using (ServiceController rrController = new ServiceController(RRServiceName))
                {
                    // Wait until RR service has started
                    rrController.WaitForStatus(ServiceControllerStatus.Running);

                    /* Unfortunately WaitForStatus returns before the service has completely started,
                     * so we have to give it a bit more time. If this isn't enough, we'll hit another
                     * exception and return to this loop, so it's safe.
                     */
                    Thread.Sleep(RRServiceSetupInterval);
                    
                    SetUpController();
                }
            }
            else
            {
                // Log and continue; problem could be temporary

                if (Program.RunFromConsole)
                {
                    Trace.TraceInformation(DateTime.Now + ": Error calling remote recorder process: {0}", e);
                    Trace.Flush();
                }
            }
        }

        private bool RecordingEquals(Recording recordingA, Recording recordingB)
        {
            if (recordingA == null && recordingB == null)
            {
                return true;
            }

            if (recordingA == null || recordingB == null)
            {
                return false;
            }

            if (recordingA.Id == recordingB.Id &&
                recordingA.Name == recordingB.Name &&
                recordingA.StartTime == recordingB.StartTime &&
                recordingA.EndTime == recordingB.EndTime)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///  Runs on a background thread to monitor the remoterecorder state and will dispatch events back to the
        ///  main thread when the state changes.
        /// </summary>
        private void BackgroundPollingWorker()
        {
            StateMachine.StateMachineInput previousState = MapRRStateToSMInput(RemoteRecorderStatus.Disconnected);
            Recording previousRecordingData = null;
            Recording previousQueuedData = null;

            Exception exceptionInRR = null;

            while (!this.shouldStop)
            {
                StateMachine.StateMachineInput? state = null;
                Recording recordingData = null;
                Recording queuedData = null;

                try
                {
                    // Get the current state from the RR process
                    state = MapRRStateToSMInput(controller.GetCurrentState().Status);

                    recordingData = controller.GetCurrentState().CurrentRecording;
                    queuedData = controller.GetNextRecording();
                }
                catch (Exception e)
                {
                    // If there's a problem with the RR, consider it disconnected and update SM
                    state = MapRRStateToSMInput(RemoteRecorderStatus.Disconnected);
                    exceptionInRR = e;
                }

                //If state changed, input new state to state machine
                if (state != previousState ||
                    !RecordingEquals(recordingData, previousRecordingData) ||
                    !RecordingEquals(queuedData, previousQueuedData))
                {
                    StateMachine.StateMachineInput stateMachineInput = (StateMachine.StateMachineInput)state;
                    var stateMachineData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) {
                            { "Recording", recordingData },
                            { "Queued", queuedData },
                        };

                    if (stateMachineInput != StateMachine.StateMachineInput.NoInput)
                    {
                        StateMachine.StateMachineInputArgs args = new StateMachine.StateMachineInputArgs(
                            stateMachineInput, stateMachineData);

                        if (stateMachineInputCallback != null)
                        {
                            stateMachineInputCallback(args);
                        }
                    }

                    previousState = stateMachineInput;
                    previousRecordingData = recordingData;
                    previousQueuedData = queuedData;
                }

                // Handle exception after SM has been updated
                if (exceptionInRR != null)
                {
                    // Blocks while RR service is not running
                    HandleRRException(exceptionInRR, true);

                    exceptionInRR = null;
                }

                // Sleep for a moment before polling again to avoid spinlock
                Thread.Sleep(RRLightProgram.Properties.Settings.Default.RecorderPollingIntervalMS);
            }
        }

        /// <summary>
        ///     Map the status from the remote recorder to our internal statemachine input
        /// </summary>
        /// <param name="rrState"></param>
        /// <returns></returns>
        private StateMachine.StateMachineInput MapRRStateToSMInput(RemoteRecorderStatus rrState)
        {
            switch (rrState)
            {
                case RemoteRecorderStatus.Stopped:
                    return StateMachine.StateMachineInput.RecorderStopped;

                case RemoteRecorderStatus.Recording:
                    return StateMachine.StateMachineInput.RecorderRecording;

                case RemoteRecorderStatus.RecorderRunning:
                    return StateMachine.StateMachineInput.RecorderRunning;

                case RemoteRecorderStatus.Previewing:
                    Recording nextRecording = controller.GetNextRecording();

                    if (nextRecording != null)
                    {
                        return StateMachine.StateMachineInput.RecorderPreviewingQueued;
                    }
                    else
                    {
                        return StateMachine.StateMachineInput.RecorderPreviewing;
                    }
                case RemoteRecorderStatus.Paused:
                    return StateMachine.StateMachineInput.RecorderPaused;

                case RemoteRecorderStatus.Faulted:
                    return StateMachine.StateMachineInput.RecorderFaulted;

                case RemoteRecorderStatus.Disconnected:
                    return StateMachine.StateMachineInput.Disconnected;

                default:
                    return StateMachine.StateMachineInput.NoInput;
            }
        }
    }
}