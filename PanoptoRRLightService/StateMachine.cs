// Uncomment the below to turn on debug output for this state machine
// #define s_debugoutput

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RRLightProgram
{
    // Abbreviations used in the transition table to make the code more readable
    using RRS = StateMachine.RRState;

    public class StateMachine
    {
        private DelcomLight light;
        private RemoteRecorderSync rrSync;

        public StateMachine(DelcomLight light, RemoteRecorderSync rrSync)
        {
            //hold onto the Light and the RemoteRecorder so we can issue actions as necessary
            this.light = light;
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
            Debug.Fail(String.Format("State machine reached an error transition: {0} {1}.", currentState, inputArgs.Input));
            throw new NotSupportedException(String.Format("State machine reached an error transition: {0} {1}.", currentState, inputArgs));
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

            control.rrSync.StopCurrentRecording();
            return true;
        }

        private static bool ActionRRStoppingRecording(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Green, false, null);

            control.rrSync.StopCurrentRecording();
            return true;
        }

        //Resume recorder after pause and turn green light on
        private static bool ActionRRResume(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Green, false, null);
            control.rrSync.ResumeCurrentRecording();

            return true;
        }

        //Pause recorder and turn blue light on
        private static bool ActionRRPause(
            StateMachine control,
            RRState currentState,
            StateMachineInputArgs inputArgs
            )
        {
            control.light.ChangeColor(DelcomColor.Yellow, false, null);
            control.rrSync.PauseCurrentRecording();

            return true;
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
            control.rrSync.StartNextRecording();

            return true;
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

        //Turn light off
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

        //Turn light off
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
            // prevent re-entrancy by queing the input if
            // we are already processing an event
            if (m_processingEvent)
            {
                if (m_stateMachineInputQueue == null)
                {
                    m_stateMachineInputQueue = new Queue<StateMachineInputArgs>();
                }
                m_stateMachineInputQueue.Enqueue(inputArgs);
            }
            else
            {
#if s_debugoutput
                Debug.WriteLine("SM State:" + m_SMState.ToString());
                Debug.WriteLine("SM Input:" + inputArgs.StateMachineInput.ToString());
#endif

                m_processingEvent = true;
                StateMachineInputArgs input = null;

                if (m_stateMachineInputQueue != null)
                {
                    input = m_stateMachineInputQueue.Dequeue();
                }
                else input = inputArgs;
                while (input != null)
                {
                    Transition transition = m_transitionTable[(int)m_SMState,
                                                              (int)input.Input];
#if s_debugoutput
                    Debug.Assert(transition.currentState == m_SMState);
                    Debug.Assert(transition.input == input.StateMachineInput);
                    Debug.Assert(transition.actionId < ActionId.LAST);
#endif
                    StateMachineAction action = m_actionTable[(int)transition.actionId];
                    bool actionSucceeded = true;
                    if (action != null)
                    {
                        actionSucceeded = action(this, State, input);
                    }
                    if (actionSucceeded)
                    {
                        RRState newState = transition.newState;

                        RRState oldState = m_SMState;

                        if (newState != m_SMState)
                        {
                            m_SMState = newState;
                        }
                    }
                    m_lastStateMachineInput = input.Input;
                    if ((m_stateMachineInputQueue != null) &&
                        (m_stateMachineInputQueue.Count > 0))
                    {
                        input = m_stateMachineInputQueue.Dequeue();
                    }
                    else
                    {
                        input = null;
                    }
                }
                m_processingEvent = false;

#if s_debugoutput
                Debug.WriteLine("SM NewState:" + m_SMState.ToString());
#endif
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
            LAST = 14,
        };

        // Must be kept in sync with enum ActionID
        private StateMachineAction[] m_actionTable =
        {
            null,
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
            },
            {
                new Transition(RRS.RRPreviewing,       StateMachineInput.NoInput,                       ActionId.Noop,                  RRS.RRPreviewing),
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
            },
            {
                new Transition(RRS.RRPreviewingQueued, StateMachineInput.NoInput,                       ActionId.Noop,                  RRS.RRPreviewingQueued),
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
            },

            {
                new Transition(RRS.RRRecording,        StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRRecording),
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
            },

            {
                new Transition(RRS.RRPaused,           StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRPaused),
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
            },
            {
                new Transition(RRS.RRStopped,          StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRPaused),
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
            },
            {
                new Transition(RRS.RRRunning,          StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRPaused),
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
            },
            {
                new Transition(RRS.RRFaulted,          StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRPaused),
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
            },

             {
                new Transition(RRS.RRDisconnected,     StateMachineInput.NoInput,                       ActionId.Noop,                 RRS.RRPaused),
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
            },
        };

        //Set initial state and input
        private RRState m_SMState = RRState.Init;

        private StateMachineInput m_lastStateMachineInput = StateMachineInput.NoInput;
        private bool m_processingEvent = false;
        private Queue<StateMachineInputArgs> m_stateMachineInputQueue = null;

        #endregion Private
    }
}