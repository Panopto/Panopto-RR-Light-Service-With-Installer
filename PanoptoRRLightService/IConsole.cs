using System;

namespace RRLightProgram
{
    /// <summary>
    /// Interface that console/serial device logic must implement.
    /// State machine may hold an instance of this interface and call the methods by state machine's actions.
    /// </summary>
    public interface IConsole
    {
        /// <summary>
        /// Output a message
        /// </summary>
        /// <param name="message">Message to be sent</param>
        void Output(String str);
    }
}
