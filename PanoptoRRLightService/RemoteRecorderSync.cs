using Panopto.RemoteRecorderAPI.V1;
using System;
using System.ComponentModel;
using System.ServiceModel;
using System.Threading;

namespace RRLightProgram
{
    public delegate void RemoteRecorderEventHandler(object sender, StateMachine.StateMachineInputArgs e);

    public class RemoteRecorderSync
    {
        private IRemoteRecorderController controller;
        private bool shouldStop = false;
        private MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback;

        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="stateMachineInputCallback"></param>
        public RemoteRecorderSync(MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback)
        {
            /* Service stuff */
            ChannelFactory<IRemoteRecorderController> channelFactory = new ChannelFactory<IRemoteRecorderController>(
                new NetNamedPipeBinding(),
                new EndpointAddress(Constants.ControllerEndpoint));
            this.controller = channelFactory.CreateChannel();

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
                // Log and continue

                //TODO Log
                Console.WriteLine("Error calling remote recorder process: {0}", e);
            }

            return result;
        }

        /// <summary>
        ///     Stop the current recording
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
                // Log and continue

                //TODO Log
                Console.WriteLine("Error calling remote recorder process: {0}", e);
            }

            return result;
        }

        /// <summary>
        ///     Stop the current recording
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
                // Log and continue

                //TODO Log
                Console.WriteLine("Error calling remote recorder process: {0}", e);
            }

            return result;
        }

        /// <summary>
        ///     Stop the current recording
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
                // Log and continue

                //TODO Log
                Console.WriteLine("Error calling remote recorder process: {0}", e);
            }

            return result;
        }

        /// <summary>
        ///     Stop the current recording
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
                // Log and continue

                //TODO Log
                Console.WriteLine("Error calling remote recorder process: {0}", e);
            }

            return result;
        }

        /// <summary>
        ///  Runs on a background thread to monitor the remoterecorder state and will dispatch events back to the
        ///  main thread when the state changes.
        /// </summary>
        private void BackgroundPollingWorker()
        {
            StateMachine.StateMachineInput previousState = MapRRStateToSMInput(RemoteRecorderStatus.Disconnected);

            while (!this.shouldStop)
            {
                if (controller == null)
                {
                    Thread.Sleep(1000);
                }
                else
                {
                    try
                    {
                        // Get the current state from the RR process
                        StateMachine.StateMachineInput state = MapRRStateToSMInput(controller.GetCurrentState().Status);

                        //If state changed, input new state to state machine
                        if (state != previousState)
                        {
                            StateMachine.StateMachineInput stateMachineInput = state;

                            if (stateMachineInput != StateMachine.StateMachineInput.NoInput)
                            {
                                StateMachine.StateMachineInputArgs args = new StateMachine.StateMachineInputArgs(stateMachineInput);

                                if (stateMachineInputCallback != null)
                                {
                                    stateMachineInputCallback(args);
                                }
                            }

                            previousState = state;
                        }
                    }
                    catch (Exception e)
                    {
                        // Log and continue, could be a temporary problem

                        //TODO Log
                        Console.WriteLine("Error calling remote recorder process: {0}", e);
                    }

                    // Sleep for a moment before polling again to avoid spinlock
                    Thread.Sleep(RRLightProgram.Properties.Settings.Default.RecorderPollingIntervalMS);
                }
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