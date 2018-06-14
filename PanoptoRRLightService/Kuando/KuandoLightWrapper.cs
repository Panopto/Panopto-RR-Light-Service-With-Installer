using Busylight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RRLightProgram
{
    class KuandoLightWrapper
    {
        #region variables
        /// <summary>
        /// Kuando busylight object. Controls turning light on/off and color
        /// </summary>
        private Busylight.SDK busylight;

        #endregion

        #region Device Setup

        /// <summary>
        /// Constructor
        /// </summary>
        public KuandoLightWrapper()
        {
            this.busylight = new Busylight.SDK();
            this.TurnOffAllLights();
        }

        /// <summary>
        /// Close the wrapper and turn off lights
        /// </summary>
        public void Close()
        {
            this.TurnOffAllLights();

            this.CloseDevice();
        }

        /// <summary>
        /// Close Kuando device
        /// </summary>
        private void CloseDevice()
        {
            busylight.Terminate();
        }

        /// <summary>
        /// Turn off the connected lights
        /// </summary>
        private void TurnOffAllLights()
        {
            busylight.Light(BusylightColor.Off);
        }

        #endregion

        #region Light control

        /// <summary>
        /// Turn on the light as a solid color
        /// </summary>
        /// <param name="color">The color to make the light</param>
        public void SetSolidLight(BusylightColor color)
        {
            this.busylight.Light(color);
        }

        /// <summary>
        /// Set the light to flash a given color
        /// </summary>
        /// <param name="color">The color to flash on the light</param>
        public void SetFlash(BusylightColor color)
        {
            //Flash the light with the given color at 500 ms intervals (1Hz)
            this.busylight.Blink(color, 5, 5);
        }

        #endregion
    }
}
