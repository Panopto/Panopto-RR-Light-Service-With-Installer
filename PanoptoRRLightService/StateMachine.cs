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
        private Queue<Input> inputQueue = new Queue<Input>();

        /// <summary>
        /// Event to be singaled when inputQueue has any entry. Set and reset should be done under the lock of inputQueue.
        /// </summary>
        private ManualResetEvent inputQueueEvent = new ManualResetEvent(initialState: false);

        /// <summary>
        /// Background thread to process inputs and transition the state.
        /// </summary>
        private Thread inputProcessThread;

        /// <summary>
        /// Event to indicate that back ground threads should stop.
        /// </summary>
        private ManualResetEvent stopRequested = new ManualResetEvent(initialState: false);

        /// <summary>
        /// Contorller of the remote recorder.
        /// This is used to request anything to the remote controller.
        /// State change is posted as state machine input, which is not a part of this interface.
        /// </summary>
        private RemoteRecorderSync remoteRecorder;

        /// <summary>
        /// Interface to control the light. Current logic may drive only one light control.
        /// This cannot be null. Emtpy implementation is attached if the caller does not pass it.
        /// </summary>
        private ILightControl light;

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
        public void Start(RemoteRecorderSync remoteRecorder, ILightControl lightControl)
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

            this.inputProcessThread = new Thread(this.InputProcessLoop);
            Trace.TraceInformation("State machine is starting.");
            inputProcessThread.Start();
        }

        /// <summary>
        /// Stop processing thread.
        /// </summary>
        public void Stop()
        {
            if (this.inputProcessThread != null)
            {
                throw new ApplicationException("StateMachine.Stop() is called while not running.");
            }

            Trace.TraceInformation("State machine is being stopped.");
            this.stopRequested.Set();
            this.inputProcessThread.Join();
            this.inputProcessThread = null;
            Trace.TraceInformation("State machine has stopped.");
        }

        #endregion Initialization and cleanup

        #region IStateMachine

        public void PostInput(Input input)
        {
            Trace.TraceInformation("Post: Input {0}", input);
            lock (this.inputQueue)
            {
                this.inputQueue.Enqueue(input);
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
                Input input;
                lock (this.inputQueue)
                {
                    if (this.inputQueue.Count == 0)
                    {
                        // Phantom event with some reason. Wait for next input.
                        this.inputQueueEvent.Reset();
                        continue;
                    }

                    input = inputQueue.Dequeue();
                    if (this.inputQueue.Count == 0)
                    {
                        this.inputQueueEvent.Reset();
                    }
                }

                // Look up the transition table.
                Transition transition = this.transitionTable[new Condition(this.state, input)];

                // Invoke the action.
                Trace.TraceInformation("Invoking {0} (Input:{1} State:{2}->{3})", transition.ActionMethod.Method.Name, input, this.state, transition.NextState);
                if (transition.ActionMethod(input))
                {
                    this.state = transition.NextState;
                }
                else
                {
                    Trace.TraceError("{0} failed. State stays at {1}", transition.ActionMethod.Method.Name, this.state);
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
        /// Turn off the light.
        /// </summary>
        private bool ActionRespondStopped(Input input)
        {
            this.light.SetSolid(LightColor.Off);
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
                if (currentState == State.RRPaused)
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
                Trace.TraceWarning("Failed to pause the recording. Flash red for 2 seconds and change light back to paused state.");
                this.light.SetFlash(LightColor.Red);
                Thread.Sleep(2000);
                this.light.SetSolid(LightColor.Yellow);
            }
            return requestResult;
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
        /// Turn green light on.
        /// </summary>
        private bool ActionRespondRecording(Input input)
        {
            this.light.SetSolid(LightColor.Green);
            return true;
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
                Trace.TraceInformation("Failed to start a new recording because detected Remote Recorder version does not accept external request.");
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
        private bool ActionRespondFaultOrDisconnected(Input input)
        {
            this.light.SetSolid(LightColor.Red);
            return true;
        }

        /// <summary>
        /// Turn light off.
        /// </summary>
        private bool ActionRespondRunning(Input input)
        {
            this.light.SetSolid(LightColor.Off);
            return true;
        }

        /// <summary>
        /// Turn light red.
        /// </summary>
        private bool ActionRespondButtonDownForUnavailableOperation(Input input)
        {
            this.light.SetSolid(LightColor.Red);
            return true;
        }

        /// <summary>
        /// Turn light off.
        /// </summary>
        private bool ActionRespondButtonUpForUnavailableOperation(Input input)
        {
            this.light.SetSolid(LightColor.Off);
            return true;
        }

        /// <summary>
        /// Turn light red if new recording is not supported.
        /// </summary>
        private bool ActionRespondButtonDownInPreviewinging(Input input)
        {
            if (!this.remoteRecorder.SupportsStartNewRecording)
            {
                this.light.SetSolid(LightColor.Red);
            }
            return true;
        }

        /// <summary>
        /// Turn light off.
        /// </summary>
        private bool ActionRespondButtonUpInPreviewinging(Input input)
        {
            if (!this.remoteRecorder.SupportsStartNewRecording)
            {
                this.light.SetSolid(LightColor.Off);
            }
            return true;
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

            this.transitionTable.Add(new Condition(State.Init, Input.NoInput), new Transition(this.ActionNone, State.Init));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.Init, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.Init, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.Init, Input.ButtonPressed), new Transition(this.ActionNone, State.Init));
            this.transitionTable.Add(new Condition(State.Init, Input.ButtonHeld), new Transition(this.ActionNone, State.Init));
            this.transitionTable.Add(new Condition(State.Init, Input.ButtonDown), new Transition(this.ActionNone, State.Init));
            this.transitionTable.Add(new Condition(State.Init, Input.ButtonUp), new Transition(this.ActionNone, State.Init));

            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.NoInput), new Transition(this.ActionNone, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderPreviewing), new Transition(this.ActionNone, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderPreviewingQueued), new Transition(this.ActionNone, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.ButtonPressed), new Transition(this.ActionNone, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.ButtonHeld), new Transition(this.ActionRequestNewRecording, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.ButtonDown), new Transition(this.ActionRespondButtonDownInPreviewinging, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPreviewing, Input.ButtonUp), new Transition(this.ActionRespondButtonUpInPreviewinging, State.RRPreviewing));

            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.NoInput), new Transition(this.ActionNone, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderPreviewing), new Transition(this.ActionNone, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderPreviewingQueued), new Transition(this.ActionNone, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.ButtonPressed), new Transition(this.ActionRequestNextRecording, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.ButtonHeld), new Transition(this.ActionRequestNextRecording, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.ButtonDown), new Transition(this.ActionNone, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRPreviewingQueued, Input.ButtonUp), new Transition(this.ActionNone, State.RRPreviewingQueued));

            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.NoInput), new Transition(this.ActionNone, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderFaulted), new Transition(this.ActionNone, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.ButtonPressed), new Transition(this.ActionNone, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.ButtonHeld), new Transition(this.ActionNone, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.ButtonDown), new Transition(this.ActionNone, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRRecordingWait, Input.ButtonUp), new Transition(this.ActionNone, State.RRRecordingWait));

            this.transitionTable.Add(new Condition(State.RRRecording, Input.NoInput), new Transition(this.ActionNone, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderRecording), new Transition(this.ActionNone, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.ButtonPressed), new Transition(this.ActionRequestPause, State.RRPausedWait));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.ButtonHeld), new Transition(this.ActionRequestStop, State.RRStoppingRecord));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.ButtonDown), new Transition(this.ActionNone, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRRecording, Input.ButtonUp), new Transition(this.ActionNone, State.RRRecording));

            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.NoInput), new Transition(this.ActionNone, State.RRPausedWait));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderFaulted), new Transition(this.ActionNone, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.ButtonPressed), new Transition(this.ActionNone, State.RRPausedWait));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.ButtonHeld), new Transition(this.ActionNone, State.RRPausedWait));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.ButtonDown), new Transition(this.ActionNone, State.RRPausedWait));
            this.transitionTable.Add(new Condition(State.RRPausedWait, Input.ButtonUp), new Transition(this.ActionNone, State.RRPausedWait));

            this.transitionTable.Add(new Condition(State.RRPaused, Input.NoInput), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderPaused), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.ButtonPressed), new Transition(this.ActionRequestResume, State.RRRecordingWait));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.ButtonHeld), new Transition(this.ActionRequestStop, State.RRStoppingPaused));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.ButtonDown), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRPaused, Input.ButtonUp), new Transition(this.ActionNone, State.RRPaused));

            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.NoInput), new Transition(this.ActionNone, State.RRStoppingPaused));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.ButtonPressed), new Transition(this.ActionNone, State.RRStoppingPaused));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.ButtonHeld), new Transition(this.ActionNone, State.RRStoppingPaused));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.ButtonDown), new Transition(this.ActionNone, State.RRStoppingPaused));
            this.transitionTable.Add(new Condition(State.RRStoppingPaused, Input.ButtonUp), new Transition(this.ActionNone, State.RRStoppingPaused));

            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.NoInput), new Transition(this.ActionNone, State.RRStoppingRecord));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.ButtonPressed), new Transition(this.ActionNone, State.RRStoppingRecord));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.ButtonHeld), new Transition(this.ActionNone, State.RRStoppingRecord));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.ButtonDown), new Transition(this.ActionNone, State.RRStoppingRecord));
            this.transitionTable.Add(new Condition(State.RRStoppingRecord, Input.ButtonUp), new Transition(this.ActionNone, State.RRStoppingRecord));

            this.transitionTable.Add(new Condition(State.RRStopped, Input.NoInput), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderStopped), new Transition(this.ActionNone, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.ButtonPressed), new Transition(this.ActionNone, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.ButtonHeld), new Transition(this.ActionNone, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.ButtonDown), new Transition(this.ActionNone, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRStopped, Input.ButtonUp), new Transition(this.ActionNone, State.RRStopped));

            this.transitionTable.Add(new Condition(State.RRRunning, Input.NoInput), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderFaulted), new Transition(this.ActionRespondFaultOrDisconnected, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.RecorderRunning), new Transition(this.ActionNone, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.ButtonPressed), new Transition(this.ActionNone, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.ButtonHeld), new Transition(this.ActionNone, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.ButtonDown), new Transition(this.ActionRespondButtonDownForUnavailableOperation, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRRunning, Input.ButtonUp), new Transition(this.ActionRespondButtonUpForUnavailableOperation, State.RRRunning));

            this.transitionTable.Add(new Condition(State.RRFaulted, Input.NoInput), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderFaulted), new Transition(this.ActionNone, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.Disconnected), new Transition(this.ActionRespondFaultOrDisconnected, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.ButtonPressed), new Transition(this.ActionNone, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.ButtonHeld), new Transition(this.ActionNone, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.ButtonDown), new Transition(this.ActionRespondButtonDownForUnavailableOperation, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRFaulted, Input.ButtonUp), new Transition(this.ActionRespondButtonUpForUnavailableOperation, State.RRFaulted));

            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.NoInput), new Transition(this.ActionNone, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderPreviewing), new Transition(this.ActionRespondPreviewing, State.RRPreviewing));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderRecording), new Transition(this.ActionRespondRecording, State.RRRecording));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderPaused), new Transition(this.ActionRespondPaused, State.RRPaused));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderFaulted), new Transition(this.ActionNone, State.RRFaulted));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderPreviewingQueued), new Transition(this.ActionRespondPreviewing, State.RRPreviewingQueued));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderStopped), new Transition(this.ActionRespondStopped, State.RRStopped));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.RecorderRunning), new Transition(this.ActionRespondRunning, State.RRRunning));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.Disconnected), new Transition(this.ActionNone, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.ButtonPressed), new Transition(this.ActionNone, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.ButtonHeld), new Transition(this.ActionNone, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.ButtonDown), new Transition(this.ActionRespondButtonDownForUnavailableOperation, State.RRDisconnected));
            this.transitionTable.Add(new Condition(State.RRDisconnected, Input.ButtonUp), new Transition(this.ActionRespondButtonUpForUnavailableOperation, State.RRDisconnected));
        }

        #endregion State machine transition table

        #region Empty ILightControl

        class EmptyLightControl : ILightControl
        {
            public void SetSolid(LightColor color) { }
            public void SetFlash(LightColor color) { }
        }

        #endregion Empty ILightControl
    }
}
