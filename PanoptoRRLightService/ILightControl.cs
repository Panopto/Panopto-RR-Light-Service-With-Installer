using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RRLightProgram
{
    /// <summary>
    /// Interface that light device logic must implement.
    /// State machine may hold an instance of this interface and call the methods by state machine's actions.
    /// Each method must NOT block.
    /// State machine continues to proceed even the light has any failure.
    /// </summary>
    public interface ILightControl
    {
        /// <summary>
        /// Direct to turn on or off the light with the specified color.
        /// </summary>
        /// <param name="color">Color to be set, or Off.</param>
        void SetSolid(LightColor color);

        /// <summary>
        /// Direct to flash the light the specified color.
        /// </summary>
        /// <param name="color">Color to be used. Cannot be Off.</param>
        void SetFlash(LightColor color);
    }

    /// <summary>
    /// List of light colors.
    /// </summary>
    public enum LightColor
    {
        Off,
        Red,
        Green,
        Yellow,
    }
}
