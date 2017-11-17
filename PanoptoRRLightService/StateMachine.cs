using Panopto.RemoteRecorderAPI.V1;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RRLightProgram
{
    public class StateMachine : IStateMachine
    {
        #region Variables

        /// <summary>
        /// Current state. Because this is updated only by InputProcessLoop method, lock is not used.
        /// </summary>
        private State state = State.Init;

        /// <summary>
        /// FIFO queue to store incoming inputs.
        /// </summary>
        private Queue<Tuple<ulong, Input>> inputQueue = new Queue<Tuple<ulong, Input>>();
        /// <summary>
        /// Counter for the input queue events
        /// </summary>
        ulong c_currentInputId = 0;

        /// <summary>
        /// Event to be signaled when inputQueue has any entry. Set and reset should be done under the lock of inputQueue.
        /// </summary>
        private ManualResetEvent inputQueueEvent = new ManualResetEvent(initialState: false);

        /// <summary>
        /// Background thread to process inputs and transition the state.
        /// </summary>
        private Thread inputProcessThread;

        /// <summary>
        /// Event to indicate that background threads should stop.
        /// </summary>
        private ManualResetEvent stopRequested = new ManualResetEvent(initialState: false);

        /// <summary>
        /// Controller of the remote recorder.
        /// This is used to request anything to the remote controller.
        /// State change is posted as state machine input, which is not a part of this interface.
        /// </summary>
        private RemoteRecorderSync remoteRecorder;

        /// <summary>
        /// Interface to control the light. Current logic may drive only one light control.
        /// This cannot be null. Empty implementation is attached if the caller does not pass it.
        /// </summary>
        private ILightControl light;

        /// <summary>
        /// Timer used to ensure recordings are cleaned up if opt-in does not occur, when in opt-in-required mode
        /// </summary>
        private Timer optInTimer;

        /// <summary>
        /// Interface to receive the result of input operation.
        /// This cannot be null. Empty implementation is attached if the caller does not pass it.
        /// </summary>
        private IInputResultReceiver resultReceiver;

        #endregion Variables

        #region Initialization and cleanup

        /// <summary>
        /// Constructor. Always succeeds.
        /// </summary>
        public StateMachine()
        {
            this.InitializeTransitionTable();
        }

        /// <summary>
        /// Set ILightControl interface, RemoteRecoderSync, and start processing thread.
        /// </summary>
        /// <param name="remoteRecorder">Remote recorder controller. Cannot be null.</param>
        /// <param name="lightControl">Light control interface. May be null.</param>
        /// <param name="resultReceiver">Result receiver interface. May be null.</param>
        public void Start(RemoteRecorderSync remoteRecorder, ILightControl lightControl, IInputResultReceiver resultReceiver)
        {
            if (remoteRecorder == null)
            {
                throw new ArgumentException("remoteRecorder cannot be null.");
            }
            if (this.inputProcessThread != null)
            {
                throw new ApplicationException("StateMachine.Start() is called while running.");
            }

            this.remoteRecorder = remoteRecorder;
            this.light = lightControl ?? new EmptyLightControl();
            this.resultReceiver = resultReceiver ?? new EmptyConsole();

            this.inputProcessThread = new Thread(this.InputProcessLoop);
            TraceVerbose.Trace("State machine is starting.");
            inputProcessThread.Start();
        }

        /// <summary>
        /// Stop processing thread.
        /// </summary>
        public void Stop()
        {
            if (this.inputProcessThread == null)
            {
                throw new ApplicationException("StateMachine.Stop() is called while not running.");
            }

            TraceVerbose.Trace("State machine is being stopped.");
            this.stopRequested.Set();
            this.inputProcessThread.Join();
            this.inputProcessThread = null;
            TraceVerbose.Trace("State machine has stopped.");
        }

        #endregion Initialization and cleanup

        #region IStateMachine

        public void PostInput(Input input)
        {
            Trace.TraceInformation("{0:hh:mm:ss:ffff} Post: Input:{1}", DateTime.UtcNow, input);
            lock (this.inputQueue)
            {
                if (c_currentInputId == ulong.MaxValue)
                {
                    c_currentInputId = 0;
                }
                this.inputQueue.Enqueue(new Tuple<ulong, Input>(++c_currentInputId, input));
                Trace.TraceInformation("{0:hh:mm:ss:ffff} Post: Id:{1}, Input:{2} queued", DateTime.UtcNow, c_currentInputId, input);
                this.inputQueueEvent.Set();
            }
        }

        public State GetCurrentState()
        {
            return this.state;
        }

        #endregion

        #region Input Process Loop

        /// <summary>
        /// Process queued inputs in serialized FIFO order.
        /// </summary>
        private void InputProcessLoop()
        {
            while (true)
            {
                int indexFired = WaitHandle.WaitAny(new WaitHandle[] { this.stopRequested, this.inputQueueEvent });

                if (indexFired == 0)
                {
                    // this.stopRequested fired. Exit the thread.
                    break;
                }

                // Pull an input from input queue.
                Tuple<ulong, Input> inputQueuItem;
                lock (this.inputQueue)
                {
                    if (this.inputQueue.Count == 0)
                    {
                        // Phantom event with some reason. Wait for next input.
                        this.inputQueueEvent.Reset();
                        continue;
                    }

                    inputQueuItem = inputQueue.Dequeue();
                    if (this.inputQueue.Count == 0)
                    {
                        this.inputQueueEvent.Reset();
                    }
                }

                Trace.TraceInformation("{0:hh:mm:ss:ffff} Processing: Id:{1}, Input:{2}, Current State {3}", DateTime.UtcNow, inputQueuItem.Item1, inputQueuItem.Item2, this.state);
                Input input = inputQueuItem.Item2;

                // Look up the transition table.
                Transition transition;
                if (this.transitionTable.TryGetValue(new Condition(this.state, input), out transition))
                {
                    // Invoke the action.
                    Trace.TraceInformation("Invoking {0} (Input:{1} State:{2}->{3})", transition.ActionMethod.Method.Name, input, this.state, transition.NextState);
                    if (transition.ActionMethod(input))
                    {
                        this.state = transition.NextState;
                        this.resultReceiver.OnInputProcessed(input, Result.Success);
                    }
                    else
                    {
                        Trace.TraceError("{0} failed. State stays at {1}", transition.ActionMethod.Method.Name, this.state);
                        this.resultReceiver.OnInputProcessed(input, Result.Failure);
                    }
                }
                else
                {
                    TraceVerbose.Trace("No transition for Input:{0} State:{1})", input, this.state);
                    this.resultReceiver.OnInputProcessed(input, Result.Ignored);
                }
            }
        }

        #endregion Input Process Loop

        #region State machine actions

        /// <summary>
        /// Action to be taken when input is posted.
        /// Action method is called in the process thread in serialized order.
        /// Therefore, state is not transitioned during the method and it can access this.state directly if needed.
        /// </summary>
        /// <param name="input">Input that triggerd this action.</param>
        /// <returns>false on fatal error. State is not transitioned.</returns>
        private delegate bool ActionMethod(Input input);

        /// <summary>
        /// No action is taken. State transition happens.
        /// </summary>
        /// <returns>Always true.</returns>
        private bool ActionNone(Input input)
        {
            return true;
        }

        /// <summary>
        /// Request to stop recording. Light is not changed.
        /// </summary>
        private bool ActionRequestStop(Input input)
        {
            State currentState = this.state;
            bool requestResult = this.remoteRecorder.StopCurrentRecording();
            if (!requestResult)
            {
                Trace.TraceWarning("Failed to stop the recording. Flash red for 2 seconds and change light back to original state.");
                this.light.SetFlash(LightColor.Red);
                Thread.Sleep(2000);
                if (currentState == State.Paused)
                {
                    this.light.SetSolid(LightColor.Yellow);
                }
                else
                {
                    this.light.SetSolid(LightColor.Green);
                }
            }
            return requestResult;
        }

        /// <summary>
        /// Request to resume paused recording. Light is not changed.
        /// </summary>
        private bool ActionRequestResume(Input input)
        {
            bool requestResult = this.remoteRecorder.ResumeCurrentRecording();
            if (!requestResult)
            {
                Trace.TraceWarning("Failed to resume the recording. Flash red for 2 seconds and change light back to paused state.");
                this.light.SetFlash(LightColor.Red);
                Thread.Sleep(2000);
                this.light.SetSolid(LightColor.Yellow);
            }
            return requestResult;
        }

        /// <summary>
        /// Request to pause current recording. Light is not changed.
        /// </summary>
        private bool ActionRequestPause(Input input)
        {
            bool requestResult = this.remoteRecorder.PauseCurrentRecording();
            if (!requestResult)
            {
                Trace.TraceWarning("Failed to pause the recording. Flash red for 2 seconds and change light back to recording state.");
                this.light.SetFlash(LightColor.Red);
                Thread.Sleep(2000);
                this.light.SetSolid(LightColor.Green);
            }
            return requestResult;
        }

        /// <summary>
        /// Start the next scheduled recording. Turn green light on, even though the actual recording has not started.
        /// </summary>
        private bool ActionRequestNextRecording(Input input)
        {
            this.light.SetSolid(LightColor.Green);
            bool requestResult = this.remoteRecorder.StartNextRecording();
            if (!requestResult)
            {
                Trace.TraceWarning("Failed to start the next recording. Turn on red for 2 seconds and turn off the light.");
                this.light.SetSolid(LightColor.Red);
                Thread.Sleep(2000);
                this.light.SetSolid(LightColor.Off);
            }
            return requestResult;
        }

        /// <summary>
        /// Start a new recording. Turn green light on, even though the actual recording has not started.
        /// </summary>
        private bool ActionRequestNewRecording(Input input)
        {
            bool requestResult = false;

            if (!this.remoteRecorder.SupportsStartNewRecording)
            {
                Trace.TraceInformation("Failed to start a new recording because detected Remote Recorder version does not accept it.");
            }
            else
            {
                this.light.SetSolid(LightColor.Green);
                requestResult = this.remoteRecorder.StartNewRecording();
                if (!requestResult)
                {
                    Trace.TraceWarning("Failed to start a new recording. Turn on red for 2 seconds and turn off the light.");
                }
            }

            if (!requestResult)
            {
                this.light.SetSolid(LightColor.Red);
                Thread.Sleep(2000);
                this.light.SetSolid(LightColor.Off);
            }

            return requestResult;
        }

        /// <summary>
        /// Request to extend recording. Light is not changed.
        /// </summary>
        private bool ActionRequestExtend(Input input)
        {
            State currentState = this.state;
            bool requestResult = this.remoteRecorder.ExtendCurrentRecording();
            if (!requestResult)
            {
                Trace.TraceWarning("Failed to extend the recording. Flash red for 2 seconds and change light back to original state.");
                this.light.SetFlash(LightColor.Red);
                Thread.Sleep(2000);
                if (currentState == State.Paused)
                {
                    this.light.SetSolid(LightColor.Yellow);
                }
                else
                {
                    this.light.SetSolid(LightColor.Green);
                }
            }
            return requestResult;
        }

        /// <summary>
        /// Turn off the light.
        /// </summary>
        private bool ActionRespondStopped(Input input)
        {
            this.light.SetSolid(LightColor.Off);
            return true;
        }

        /// <summary>
        /// Turn yellow light on.
        /// </summary>
        private bool ActionRespondPaused(Input input)
        {
            this.light.SetSolid(LightColor.Yellow);
            return true;
        }

        /// <summary>
        /// If we have an opt-in timer, clean it up
        /// </summary>
        private void CleanupOptInTimer()
        {
            if (this.optInTimer != null)
            {
                this.optInTimer.Dispose();
                this.optInTimer = null;
            }
        }

        /// <summary>
        /// Turn flashing green light on for a potential recording, and start a timer
        /// </summary>
        private bool ActionRespondPotentialRecording(Input input)
        {
            this.light.SetFlash(LightColor.Green);

            CleanupOptInTimer();

            this.optInTimer = new Timer(
                callback: (state) =>
                {
                    CleanupOptInTimer();

                    // Timer elapsed, send the state machine an input of 
                    this.PostInput(Input.OptInTimerElapsed);
                },
                state: null,
                dueTime: Properties.Settings.Default.OptInTimeout,
                period: TimeSpan.FromMilliseconds(-1)); // specify -1 milliseconds to disable periodic signalling

            return true;
        }

        private bool ActionRequestOptIn(Input input)
        {
            // Kill the timer, then switch the light to look like we're recording (which we are!)
            CleanupOptInTimer();

            return ActionRespondRecording(input);
        }

        /// <summary>
        /// Turn green light on.
        /// </summary>
        private bool ActionRespondRecording(Input input)
        {
            this.light.SetSolid(LightColor.Green);
            return true;
        }

        /// <summary>
        /// Turn light off.
        /// </summary>
        private bool ActionRespondPreviewing(Input input)
        {
            this.light.SetSolid(LightColor.Off);
            return true;
        }

        /// <summary>
        /// Turn red light on.
        /// </summary>
        private bool ActionRespondFaultedOrDisconnected(Input input)
        {
            this.light.SetSolid(LightColor.Red);
            return true;
        }

        /// <summary>
        /// Turn light off.
        /// </summary>
        private bool ActionRespondDormant(Input input)
        {
            this.light.SetSolid(LightColor.Off);
            return true;
        }

        //Delete method to be later changed
        private bool ActionRequestStopAndDelete(Input input)
        {
            bool requestResult = this.remoteRecorder.StopAndDeleteCurrentRecording();
            if (!requestResult)
            {
                Trace.TraceWarning("Failed to stop the recording. Flash red for 2 seconds and change light back to original state.");

                this.light.SetFlash(LightColor.Red);
                Thread.Sleep(2000);

                this.light.SetFlash(LightColor.Green);
            }

            return requestResult;
        }

        #endregion State machine actions

        #region State machine transition table

        /// <summary>
        /// Combination of current state and input.
        /// This defines a condition to trigger a transition (action and next state).
        /// </summary>
        /// 
        private struct Condition
        {
            public readonly State CurrentState;
            public readonly Input Input;
            public Condition(State currentState, Input input)
            {
                this.CurrentState = currentState;
                this.Input = input;
            }
        }

        /// <summary>
        /// Combination of action and next state.
        /// This defines the transtion for a given condition.
        /// </summary>
        private struct Transition
        {
            public readonly ActionMethod ActionMethod;
            public readonly State NextState;
            public Transition(ActionMethod actionMethod, State nextState)
            {
                this.ActionMethod = actionMethod;
                this.NextState = nextState;
            }
        }

        /// <summary>
        /// Definitions of all state transitions.
        /// If entry does not exist in this table, it is interpreted no action & state unchanged.
        /// </summary>
        private Dictionary<Condition, Transition> transitionTable = null;

        /// <summary>
        /// Fill out the transitiont table.
        /// </summary>
        private void InitializeTransitionTable()
        {
            if (this.transitionTable != null)
            {
                throw new ApplicationException("InitializeTransitionTable is called for the second time.");
            }
            this.transitionTable = new Dictionary<Condition, Transition>();

            this.transitionTable.Add(new Condition(State.Init, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionNone, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.ButtonHeld), new Transition(this.ActionRequestNewRecording, State.TransitionAnyToRecording));
            this.transitionTable.Add(new Condition(State.PreviewingNoNextSchedule, Input.CommandStart), new Transition(this.ActionRequestNewRecording, State.TransitionAnyToRecording));

            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderPaused),new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.ButtonPressed), new Transition(this.ActionRequestNextRecording, State.TransitionAnyToRecording));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.ButtonHeld), new Transition(this.ActionRequestNextRecording, State.TransitionAnyToRecording));
            this.transitionTable.Add(new Condition(State.PreviewingWithNextSchedule, Input.CommandStart), new Transition(this.ActionRequestNextRecording, State.TransitionAnyToRecording));

            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.TransitionAnyToRecording, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            // to make sure paused does not go back to potential recording in confirmation of opt-in setting
            this.transitionTable.Add(new Condition(State.TransitionPausedToRecording, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondRecording, State.Recording)); // opt-in feature
            this.transitionTable.Add(new Condition(State.TransitionPausedToRecording, Input.RecorderRecording),                 new Transition(this.ActionRespondRecording, State.Recording)); // normal feature

            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.ButtonPressed), new Transition(this.ActionRequestOptIn, State.Recording)); // Opt-in
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.ButtonHeld), new Transition(this.ActionRequestStopAndDelete, State.TransitionRecordingToStop)); // Opt-out
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.OptInTimerElapsed), new Transition(this.ActionRequestStopAndDelete, State.TransitionRecordingToStop)); // Opt-out
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.CommandStart), new Transition(this.ActionRequestOptIn, State.Recording)); // Opt-in
            this.transitionTable.Add(new Condition(State.PotentialRecording, Input.CommandStop), new Transition(this.ActionRequestStopAndDelete, State.TransitionRecordingToStop)); // Opt-out

            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.Recording, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));
            this.transitionTable.Add(new Condition(State.Recording, Input.ButtonPressed), new Transition(this.ActionRequestPause, State.TransitionRecordingToPause));
            this.transitionTable.Add(new Condition(State.Recording, Input.ButtonHeld), new Transition(this.ActionRequestStop, State.TransitionRecordingToStop));
            this.transitionTable.Add(new Condition(State.Recording, Input.CommandStop), new Transition(this.ActionRequestStop, State.TransitionRecordingToStop));
            this.transitionTable.Add(new Condition(State.Recording, Input.CommandPause), new Transition(this.ActionRequestPause, State.TransitionRecordingToPause));
            this.transitionTable.Add(new Condition(State.Recording, Input.CommandExtend), new Transition(this.ActionRequestExtend, State.Recording));

            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToPause, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondRecording, State.Recording)); // Consider all resumptions from pause to be already opted-in
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.Paused, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));
            this.transitionTable.Add(new Condition(State.Paused, Input.ButtonPressed), new Transition(this.ActionRequestResume, State.TransitionPausedToRecording)); // new state
            this.transitionTable.Add(new Condition(State.Paused, Input.ButtonHeld), new Transition(this.ActionRequestStop, State.TransitionPausedToStop));
            this.transitionTable.Add(new Condition(State.Paused, Input.CommandStop), new Transition(this.ActionRequestStop, State.TransitionPausedToStop));
            this.transitionTable.Add(new Condition(State.Paused, Input.CommandResume), new Transition(this.ActionRequestResume, State.TransitionPausedToRecording)); // new state
            this.transitionTable.Add(new Condition(State.Paused, Input.CommandExtend), new Transition(this.ActionRequestExtend, State.Paused));

            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.TransitionPausedToStop, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.TransitionRecordingToStop, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.Stopped, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
            this.transitionTable.Add(new Condition(State.Dormant, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));
            
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.Faulted, Input.RecorderDisconnected), new Transition(this.ActionRespondFaultedOrDisconnected, State.Disconnected));

            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderPreviewingNoNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingNoNextSchedule));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderPreviewingWithNextSchedule), new Transition(this.ActionRespondPreviewing, State.PreviewingWithNextSchedule));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.Recording));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderStartedPotentialRecording), new Transition(this.ActionRespondPotentialRecording, State.PotentialRecording));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.Paused));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.Stopped));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderDormant), new Transition(this.ActionRespondDormant, State.Dormant));
            this.transitionTable.Add(new Condition(State.Disconnected, Input.RecorderFaulted), new Transition(this.ActionRespondFaultedOrDisconnected, State.Faulted));
        }

        #endregion State machine transition table

        #region Empty ILightControl

        class EmptyLightControl : ILightControl
        {
            public void SetSolid(LightColor color) { }
            public void SetFlash(LightColor color) { }
        }

        #endregion Empty ILightControl

        #region Empty IInputResultReceiver

        class EmptyConsole : IInputResultReceiver
        {
            public void OnInputProcessed(Input input, Result result) { }
        }

        #endregion Empty IInputResultReceiver
    }
}
