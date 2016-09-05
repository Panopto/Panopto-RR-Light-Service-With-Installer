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
    ///  List of the inputs that the state machine accepts.
    /// </summary>
    public enum Input
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

        /// <summary>
        /// The button was pressed for less time than the threshold for holding, and is now up, resulting in a full click.
        /// </summary>
        ButtonPressed = 9,

        /// <summary>
        /// The button was held down for longer than the hold threshold.
        /// </summary>
        ButtonHeld = 10,

        /// <summary>
        /// The button is pressed down, regardless it's 'pressed' or 'held'.
        /// Note that this input is used only to turn the red light on to indicate that no recordings are queued while previewing.
        /// </summary>
        ButtonDown = 11,

        /// <summary>
        /// The button is pressed released, regardless it's 'pressed' or 'held'.
        /// Note that this input is used only turn the red light off which indicate that no recordings are queued while previewing.
        /// </summary>
        ButtonUp = 12,
    }
}
