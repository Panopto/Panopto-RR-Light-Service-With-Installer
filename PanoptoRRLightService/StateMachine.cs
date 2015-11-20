// Uncomment the below to turn on debug output for this state machine
// #define s_debugoutput

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RRLightProgram
{

    // Abbreviations used in the transition table to make the code more readable
    using RRS = StateMachine.RRState;

    public class StateMachine
    {
        private DelcomLight light;
        private SerialComm serial;
        private RemoteRecorderSync rrSync;

        public StateMachine(DelcomLight light, SerialComm serial, RemoteRecorderSync rrSync)
        {
            //hold onto the Light and the RemoteRecorder so we can issue actions as necessary
            this.light = light;
            this.serial = serial;
            this.rrSync = rrSync;
        }

        #region Public

        #region Types

        /// <summary>
        /// Base class for input event args
        /// </summary>
        public class StateMachineInputArgs : EventArgs
        {
            public StateMachineInputArgs(StateMachineInput input, Object data = null)
            {
                m_input = input;
                m_data = data;
            }

            public StateMachineInput Input
            {
                get { return m_input; }
            }

            public Object Data
            {
                get { return m_data; }
            }

            private StateMachineInput m_input;
            private object m_data;
        }

        /// <summary>
        ///  Inputs events used in the state machine to trigger a transition
        /// </summary>
        public enum StateMachineInput
        {
            NoInput = 0,
            RecorderPreviewing = 1,
            RecorderRecording = 2,
            RecorderPaused = 3,
            RecorderFaulted = 4,
            RecorderPreviewingQueued = 5,
            RecorderStopped = 6,
            RecorderRunning = 7,
            Disconnected = 8,

            //Button Pressed means that the button was pressed for less time than the threshold for holding, and is now up, resulting in a full click
            ButtonPressed = 9,

            //Button held means that the button was held down for longer than the hold threshold
            ButtonHeld = 10,

            //Button down occurs whenever the button is pressed down, and serves only to turn the red light on to indicate that no recordings are queued while previewing. It results in a noop in all other instances.
            ButtonDown = 11,

            //Button up occurs whenever the button is relesed, and serves only to turn the red light off which indicate that no recordings are queued while previewing. It results in a noop in all other instances.
            ButtonUp = 12,

            //Discrete commands, received via serial or other input
            CommandStart = 13,
            CommandStop = 14,
            CommandPause = 15,
            CommandResume = 16,
            CommandExtend = 17,
        }

        /// <summary>
        /// Enum representing the state of the control
        /// </summary>
        internal enum RRState
        {
            Init = 0,
            RRPreviewing = 1,
            RRPreviewingQueued = 2,
            RRRecordingWait = 3,
            RRRecording = 4,
            RRPausedWait = 5,
            RRPaused = 6,
            RRStoppingPaused = 7,
            RRStoppingRecord = 8,
            RRStopped = 9,
            RRRunning = 10,
            RRFaulted = 11,
            RRDisconnected = 12,
        }

        /// <summary>
        /// delegate type used to define the actions performed on a transition between states
        /// </summary>
        private delegate bool StateMachineAction(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs args
            );

        #endregion Types

        #region Properties

        /// <summary>
        /// The current state of the browsing control
        /// </summary>
        private RRState State
        {
            get
            {
                return m_SMState;
            }
        }

        #endregion Properties

        #endregion Public

        #region Private

        #region Types

        /// <summary>
        /// Recording defining a transition from currentState to
        /// newState when input is received.
        /// The delegate action is called prior to entering the new state
        /// </summary>
        private struct Transition
        {
            public readonly RRState currentState;
            public readonly StateMachineInput input;
            public readonly ActionId actionId;
            public readonly RRState newState;

            public Transition(RRState cState,
                              StateMachineInput smInput,
                              ActionId tActionId,
                              RRState nState)
            {
                currentState = cState;
                input = smInput;
                actionId = tActionId;
                newState = nState;
            }
        }

        #endregion Types

        #region Properties

        /// <summary>
        /// Last input processed by the state machine
        /// </summary>
        private StateMachineInput LastStateMachineInput
        {
            get
            {
                return m_lastStateMachineInput;
            }
        }

        #endregion Properties

        #region Methods

        #region StateMachine Actions

        /// <summary>
        /// Error transition action.
        /// Used in the transition table when processing an event type that should never occur
        /// when in the given state
        /// </summary>
        private static bool ActionError(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            Trace.Fail(String.Format("State machine reached an error transition: {0} {1}.", currentState, inputArgs.Input));
            throw new NotSupportedException(String.Format("State machine reached an error transition: {0} {1}.", currentState, inputArgs));
        }

        //Always return true
        private static bool ActionRRNoop(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            return true;
        }

        //Stop recorder and turn light off
        private static bool ActionRRStopped(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Off);
            return true;
        }

        //Stop recorder and turn light off
        private static bool ActionRRStoppingPaused(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Yellow, false, null);

            bool stopRecording = control.rrSync.StopCurrentRecording();
            if (stopRecording == false)
            {
                //If we can't stop the recording, flash red for 2 seconds and change light back to paused color before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Yellow);
            }
            return stopRecording;
        }

        private static bool ActionRRStoppingRecording(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Green, false, null);

            bool stopRecording = control.rrSync.StopCurrentRecording();
            if (stopRecording == false)
            {
                //If we can't stop the recording, flash red for 2 seconds and set light back to recording color before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Green);
            }
            return stopRecording;
        }

        //Resume recorder after pause and turn green light on
        private static bool ActionRRResume(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Green, false, null);
            bool resumeRecording = control.rrSync.ResumeCurrentRecording();
            if (resumeRecording == false)
            {
                //If we can't resume the recording, flash red for 2 seconds and set light back to paused color before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Yellow);
            }
            return resumeRecording;
        }

        //Pause recorder and turn blue light on
        private static bool ActionRRPause(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Yellow, false, null);

            bool pauseRecording = control.rrSync.PauseCurrentRecording();
            if (pauseRecording == false)
            {
                //If we can't pause the recording, flash red for 2 seconds  and set light back to off before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Off);
            }
            return pauseRecording;
        }

        //Pause recorder and turn blue light on
        private static bool ActionRRIsPaused(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Yellow);

            return true;
        }

        //Resume recorder after pause and turn green light on
        private static bool ActionRRRecording(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Green);

            return true;
        }

        //Start queued recording and turn green light on
        private static bool ActionRecordNext(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Green, false, null);

            bool startNext = control.rrSync.StartNextRecording();
            if (startNext == false)
            {
                //If we can't start the next recording, flash red for 2 seconds then return the light to off before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Off);
            }
            return startNext;
        }

        //Turn light off for preview
        private static bool ActionPreview(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Off);

            return true;
        }

        //Turn light red
        private static bool ActionRRFaultOrDisconnect(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Red);

            return true;
        }

        //Turn light off
        private static bool ActionRRRunning(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Off);

            return true;
        }

        //Turn light red
        private static bool ActionNotQueuedButtonDown(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Red);

            return true;
        }

        //Turn light off
        private static bool ActionNotQueuedButtonUp(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Off);

            return true;
        }

        //Extend active recording
        private static bool ActionExtendPaused(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            bool extendRecording = control.rrSync.ExtendCurrentRecording();
            if (extendRecording == false)
            {
                //If we can't extend the recording, flash red for 2 seconds and change light back to paused color before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Yellow);
            }
            return extendRecording;
        }

        private static bool ActionExtendRecording(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            bool extendRecording = control.rrSync.ExtendCurrentRecording();
            if (extendRecording == false)
            {
                //If we can't extend the recording, flash red for 2 seconds and set light back to recording color before returning false
                control.light.ChangeColor(DelcomColor.Red, false, TimeSpan.FromMilliseconds(2000));
                Thread.Sleep(2000);
                control.light.ChangeColor(DelcomColor.Green);
            }
            return extendRecording;
        }

        //Turn off light for ending loop
        private static bool ActionLast(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Off);

            return true;
        }

        #endregion StateMachine Actions

        private delegate void StateMachineDelegate(StateMachineInputArgs inputArgs);

        /// <summary>
        /// Process new StateMachineInput by looking up executing the appropriate
        /// transition for the current state and this input event
        /// </summary>
        public void ProcessStateMachineInput(StateMachineInputArgs inputArgs)
        {
            Transition transition = m_transitionTable[(int)m_SMState,
                                                          (int)inputArgs.Input];
            if (Program.RunFromConsole)
            {
                Trace.Assert(transition.currentState == m_SMState);
                Trace.Assert(transition.input == inputArgs.Input);
                Trace.Assert(transition.actionId < ActionId.LAST);

                Trace.TraceInformation(DateTime.Now + ": SM State:" + m_SMState.ToString());
                Trace.TraceInformation(DateTime.Now + ": SM Input:" + inputArgs.Input.ToString());
                Trace.Flush();
            }

            StateMachineAction action = m_actionTable[(int)transition.actionId];
                
            if (action(this, State, inputArgs))
            {
                m_SMState = transition.newState;
                this.serial.SerialOutput(inputArgs.Input.ToString() + " OK");
            }
            else
            {
                this.serial.SerialOutput(inputArgs.Input.ToString() + " ERROR");
            }

            m_lastStateMachineInput = inputArgs.Input;

            if (Program.RunFromConsole)
            {
                Trace.TraceInformation(DateTime.Now + ": SM NewState:" + m_SMState.ToString());
                Trace.Flush();
            }
        }

        #endregion Methods

        /// <summary>
        /// Enumeration of the actions performed by the state machine
        /// Needs to be kept in sync with the array of actions m_actionTable
        /// </summary>
        private enum ActionId
        {
            Noop = 0,
            Stop = 1,
            StoppingPaused = 2,
            StoppingRecording = 3,
            Resume = 4,
            Pause = 5,
            IsPaused = 6,
            Recording = 7,
            Next = 8,
            Preview = 9,
            FaultDisconnect = 10,
            Running = 11,
            CantRecordButtonDown = 12,
            CantRecordButtonUp = 13,
            ExtendPaused = 14,
            ExtendRecording = 15,
            LAST = 16,
        };

        // Must be kept in sync with enum ActionID
        private StateMachineAction[] m_actionTable =
        {
            new StateMachineAction(ActionRRNoop),
            new StateMachineAction(ActionRRStopped),
            new StateMachineAction(ActionRRStoppingPaused),
            new StateMachineAction(ActionRRStoppingRecording),
            new StateMachineAction(ActionRRResume),
            new StateMachineAction(ActionRRPause),
            new StateMachineAction(ActionRRIsPaused),
            new StateMachineAction(ActionRRRecording),
            new StateMachineAction(ActionRecordNext),
            new StateMachineAction(ActionPreview),
            new StateMachineAction(ActionRRFaultOrDisconnect),
            new StateMachineAction(ActionRRRunning),
            new StateMachineAction(ActionNotQueuedButtonDown),
            new StateMachineAction(ActionNotQueuedButtonUp),
            new StateMachineAction(ActionExtendPaused),
            new StateMachineAction(ActionExtendRecording),
            new StateMachineAction(ActionLast),
        };

        /// <summary>
        /// Transition table for the state machine
        /// that maps the current state and inputevent
        /// to the appropriate transition action and new state
        /// </summary>
        private static Transition[,] m_transitionTable =
        {
            // The first two fields are just for convenience and debugging
            //  Current State                          Incoming Event                                   Transition Action              Resulting State
            {
                new Transition(RRS.Init,               StateMachineInput.NoInput,                       ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.RecorderPreviewing,            ActionId.Preview,               RRS.RRPreviewing),
                new Transition(RRS.Init,               StateMachineInput.RecorderRecording,             ActionId.Recording,             RRS.RRRecording),
                new Transition(RRS.Init,               StateMachineInput.RecorderPaused,                ActionId.IsPaused,              RRS.RRPaused),
                new Transition(RRS.Init,               StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,       RRS.RRFaulted),
                new Transition(RRS.Init,               StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,               RRS.RRPreviewingQueued),
                new Transition(RRS.Init,               StateMachineInput.RecorderStopped,               ActionId.Stop,                  RRS.RRStopped),
                new Transition(RRS.Init,               StateMachineInput.RecorderRunning,               ActionId.Running,               RRS.RRRunning),
                new Transition(RRS.Init,               StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,       RRS.RRDisconnected),
                new Transition(RRS.Init,               StateMachineInput.ButtonPressed,                 ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.ButtonHeld,                    ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.ButtonDown,                    ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.ButtonUp,                      ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.CommandStart,                  ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.CommandStop,                   ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.CommandPause,                  ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.CommandResume,                 ActionId.Noop,                  RRS.Init),
                new Transition(RRS.Init,               StateMachineInput.CommandExtend,                 ActionId.Noop,                  RRS.Init),
            },
            {
                new Transition(RRS.RRPreviewing,       StateMachineInput.NoInput,                       ActionId.Preview,               RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderPreviewing,            ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderRecording,             ActionId.Recording,             RRS.RRRecording),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderPaused,                ActionId.IsPaused,              RRS.RRPaused),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,       RRS.RRFaulted),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderPreviewingQueued,      ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderStopped,               ActionId.Stop,                  RRS.RRStopped),
                new Transition(RRS.RRPreviewing,       StateMachineInput.RecorderRunning,               ActionId.Running,               RRS.RRRunning),
                new Transition(RRS.RRPreviewing,       StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,       RRS.RRDisconnected),
                new Transition(RRS.RRPreviewing,       StateMachineInput.ButtonPressed,                 ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.ButtonHeld,                    ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.ButtonDown,                    ActionId.CantRecordButtonDown,   RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.ButtonUp,                      ActionId.CantRecordButtonUp,     RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.CommandStart,                  ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.CommandStop,                   ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.CommandPause,                  ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.CommandResume,                 ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewing,       StateMachineInput.CommandExtend,                 ActionId.Noop,                  RRS.RRPreviewing),
            },
            {
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.NoInput,                       ActionId.Preview,               RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderPreviewing,            ActionId.Noop,                  RRS.RRPreviewing),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderRecording,             ActionId.Recording,             RRS.RRRecording),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderPaused,                ActionId.IsPaused,              RRS.RRPaused),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,       RRS.RRFaulted),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderPreviewingQueued,      ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderStopped,               ActionId.Stop,                  RRS.RRStopped),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.RecorderRunning,               ActionId.Running,               RRS.RRRunning),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,       RRS.RRDisconnected),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.ButtonPressed,                 ActionId.Next,                  RRS.RRRecordingWait),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.ButtonHeld,                    ActionId.Next,                  RRS.RRRecordingWait),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.ButtonDown,                    ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.ButtonUp,                      ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.CommandStart,                  ActionId.Next,                  RRS.RRRecordingWait),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.CommandStop,                   ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.CommandPause,                  ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.CommandResume,                 ActionId.Noop,                  RRS.RRPreviewingQueued),
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.CommandExtend,                 ActionId.Noop,                  RRS.RRPreviewingQueued),
            },

             {
                new Transition(RRS.RRRecordingWait,     StateMachineInput.NoInput,                      ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderPreviewing,           ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderRecording,            ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderPaused,               ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderFaulted,              ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderPreviewingQueued,     ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderStopped,              ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.RecorderRunning,              ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.Disconnected,                 ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.ButtonPressed,                ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.ButtonHeld,                   ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.ButtonDown,                   ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.ButtonUp,                     ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.CommandStart,                 ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.CommandStop,                  ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.CommandPause,                 ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.CommandResume,                ActionId.Noop,                 RRS.RRRecordingWait),
                new Transition(RRS.RRRecordingWait,     StateMachineInput.CommandExtend,                ActionId.Noop,                 RRS.RRRecordingWait),
            },

            {
                new Transition(RRS.RRRecording,        StateMachineInput.NoInput,                       ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderRecording,             ActionId.Noop,                 RRS.RRRecording),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRRecording,        StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRRecording,        StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRRecording,        StateMachineInput.ButtonPressed,                 ActionId.Pause,                RRS.RRPausedWait),
                new Transition(RRS.RRRecording,        StateMachineInput.ButtonHeld,                    ActionId.StoppingRecording,    RRS.RRStoppingRecord),
                new Transition(RRS.RRRecording,        StateMachineInput.ButtonDown,                    ActionId.Noop,                 RRS.RRRecording),
                new Transition(RRS.RRRecording,        StateMachineInput.ButtonUp,                      ActionId.Noop,                 RRS.RRRecording),
                new Transition(RRS.RRRecording,        StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRRecording),
                new Transition(RRS.RRRecording,        StateMachineInput.CommandStop,                   ActionId.StoppingRecording,    RRS.RRStoppingRecord),
                new Transition(RRS.RRRecording,        StateMachineInput.CommandPause,                  ActionId.Pause,                RRS.RRPausedWait),
                new Transition(RRS.RRRecording,        StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRRecording),
                new Transition(RRS.RRRecording,        StateMachineInput.CommandExtend,                 ActionId.ExtendRecording,      RRS.RRRecording),
            },

            {
                new Transition(RRS.RRPausedWait,        StateMachineInput.NoInput,                      ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderPreviewing,           ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderRecording,            ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderPaused,               ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderFaulted,              ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderPreviewingQueued,     ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderStopped,              ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRPausedWait,        StateMachineInput.RecorderRunning,              ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRPausedWait,        StateMachineInput.Disconnected,                 ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRPausedWait,        StateMachineInput.ButtonPressed,                ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.ButtonHeld,                   ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.ButtonDown,                   ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.ButtonUp,                     ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.CommandStart,                 ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.CommandStop,                  ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.CommandPause,                 ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.CommandResume,                ActionId.Noop,                 RRS.RRPausedWait),
                new Transition(RRS.RRPausedWait,        StateMachineInput.CommandExtend,                ActionId.Noop,                 RRS.RRPausedWait),
            },

            {
                new Transition(RRS.RRPaused,           StateMachineInput.NoInput,                       ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderPaused,                ActionId.Noop,                 RRS.RRPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRPaused,           StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRPaused,           StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRPaused,           StateMachineInput.ButtonPressed,                 ActionId.Resume,               RRS.RRRecordingWait),
                new Transition(RRS.RRPaused,           StateMachineInput.ButtonHeld,                    ActionId.StoppingPaused,       RRS.RRStoppingPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.ButtonDown,                    ActionId.Noop,                 RRS.RRPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.ButtonUp,                      ActionId.Noop,                 RRS.RRPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.CommandStop,                   ActionId.StoppingPaused,       RRS.RRStoppingPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRPaused),
                new Transition(RRS.RRPaused,           StateMachineInput.CommandResume,                 ActionId.Resume,               RRS.RRRecordingWait),
                new Transition(RRS.RRPaused,           StateMachineInput.CommandExtend,                 ActionId.ExtendPaused,         RRS.RRPaused),
            },

              {
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.ButtonPressed,                 ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.ButtonHeld,                    ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.ButtonDown,                    ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.ButtonUp,                      ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.CommandStop,                   ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRStoppingPaused),
                new Transition(RRS.RRStoppingPaused,   StateMachineInput.CommandExtend,                 ActionId.Noop,                 RRS.RRStoppingPaused),
            },

                  {
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.ButtonPressed,                 ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.ButtonHeld,                    ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.ButtonDown,                    ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.ButtonUp,                      ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.CommandStop,                   ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRStoppingRecord),
                new Transition(RRS.RRStoppingRecord,   StateMachineInput.CommandExtend,                 ActionId.Noop,                 RRS.RRStoppingRecord),
            },
            {
                new Transition(RRS.RRStopped,          StateMachineInput.NoInput,                       ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderStopped,               ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRStopped,          StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRStopped,          StateMachineInput.ButtonPressed,                 ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.ButtonHeld,                    ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.ButtonDown,                    ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.ButtonUp,                      ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.CommandStop,                   ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRStopped),
                new Transition(RRS.RRStopped,          StateMachineInput.CommandExtend,                 ActionId.Noop,                 RRS.RRStopped),
            },
            {
                new Transition(RRS.RRRunning,          StateMachineInput.NoInput,                       ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderFaulted,               ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRRunning,          StateMachineInput.RecorderRunning,               ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRRunning,          StateMachineInput.ButtonPressed,                 ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.ButtonHeld,                    ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.ButtonDown,                    ActionId.CantRecordButtonDown, RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.ButtonUp,                      ActionId.CantRecordButtonUp,   RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.CommandStop,                   ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRRunning),
                new Transition(RRS.RRRunning,          StateMachineInput.CommandExtend,                 ActionId.Noop,                 RRS.RRRunning),
            },
            {
                new Transition(RRS.RRFaulted,          StateMachineInput.NoInput,                       ActionId.FaultDisconnect,      RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderFaulted,               ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRFaulted,          StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRFaulted,          StateMachineInput.Disconnected,                  ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRFaulted,          StateMachineInput.ButtonPressed,                 ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.ButtonHeld,                    ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.ButtonDown,                    ActionId.CantRecordButtonDown, RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.ButtonUp,                      ActionId.CantRecordButtonUp,   RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.CommandStop,                   ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRFaulted,          StateMachineInput.CommandExtend,                 ActionId.Noop,                 RRS.RRFaulted),
            },

             {
                new Transition(RRS.RRDisconnected,     StateMachineInput.NoInput,                       ActionId.FaultDisconnect,      RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderPreviewing,            ActionId.Preview,              RRS.RRPreviewing),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderRecording,             ActionId.Recording,            RRS.RRRecording),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderPaused,                ActionId.IsPaused,             RRS.RRPaused),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderFaulted,               ActionId.Noop,                 RRS.RRFaulted),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderPreviewingQueued,      ActionId.Preview,              RRS.RRPreviewingQueued),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderStopped,               ActionId.Stop,                 RRS.RRStopped),
                new Transition(RRS.RRDisconnected,     StateMachineInput.RecorderRunning,               ActionId.Running,              RRS.RRRunning),
                new Transition(RRS.RRDisconnected,     StateMachineInput.Disconnected,                  ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.ButtonPressed,                 ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.ButtonHeld,                    ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.ButtonDown,                    ActionId.CantRecordButtonDown, RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.ButtonUp,                      ActionId.CantRecordButtonUp,   RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.CommandStart,                  ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.CommandStop,                   ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.CommandPause,                  ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.CommandResume,                 ActionId.Noop,                 RRS.RRDisconnected),
                new Transition(RRS.RRDisconnected,     StateMachineInput.CommandExtend,                 ActionId.Noop,                 RRS.RRDisconnected),
            },
        };

        //Set initial state and input
        private RRState m_SMState = RRState.Init;

        private StateMachineInput m_lastStateMachineInput = StateMachineInput.NoInput;

        #endregion Private
    }
}