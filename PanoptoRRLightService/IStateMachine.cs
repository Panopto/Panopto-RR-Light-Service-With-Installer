using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RRLightProgram
{
    /// <summary>
    /// Interface that components may post the input to the state machine.
    /// 
    /// Optionally, the components may get the current state.
    /// This part is not used by the current code, but prepared for the future extension.
    /// </summary>
    public interface IStateMachine
    {
        /// <summary>
        /// Post an input to the state machine.
        /// </summary>
        void PostInput(Input input);

        /// <summary>
        /// Get the current state of the state machine.
        /// </summary>
        State GetCurrentState();
    }

    /// <summary>
    /// List of the states of the state machine.
    /// </summary>
    public enum State
    {
        Init,
        PreviewingNoNextSchedule,
        PreviewingWithNextSchedule,
        TransitionAnyToRecording,
        Recording,
        TransitionRecordingToPause,
        Paused,
        TransitionPausedToStop,
        TransitionRecordingToStop,
        Stopped,
        Dormant,
        Faulted,
        Disconnected,
    }

    /// <summary>
    /// List of the inputs that the state machine accepts.
    /// </summary>
    public enum Input
    {
        None,

        RecorderPreviewingNoNextSchedule,
        RecorderPreviewingWithNextSchedule,
        RecorderRecording,
        RecorderPaused,
        RecorderStopped,

        /// <summary>
        /// Recoder may be in a state that is running, but not recording or previewing.
        /// It may happen in a situation, for example, Windows Recorder takes the control
        /// of recording component and Remote Recorder may not take any action.
        /// </summary>
        RecorderDormant,

        RecorderFaulted,
        RecorderDisconnected,

        /// <summary>
        /// The button was pressed for less time than the threshold for holding, and is now up.
        /// This results in a full click.
        /// </summary>
        ButtonPressed,

        /// <summary>
        /// The button was held down for longer than the hold threshold.
        /// After this is fired, ButtonPressed must not fired when the button is up.
        /// </summary>
        ButtonHeld,

        /// <summary>
        /// The button is pressed down, regardless it's 'pressed' or 'held'.
        /// </summary>
        ButtonDown,

        /// <summary>
        /// The button is pressed released, regardless it's 'pressed' or 'held'.
        /// </summary>
        ButtonUp,
    }
}
