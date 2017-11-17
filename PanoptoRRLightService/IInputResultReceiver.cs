using System;

namespace RRLightProgram
{
    /// <summary>
    /// Interface to receive the result of input.
    /// State machine may hold an instance of this interface and call the methods by state machine's actions.
    /// </summary>
    public interface IInputResultReceiver
    {
        /// <summary>
        /// Method called after an input is procssed.
        /// </summary>
        void OnInputProcessed(Input input, Result result);
    }

    /// <summary>
    /// Result of processing an input.
    /// </summary>
    public enum Result
    {
        Success,
        Failure,
        Ignored
    }
}
