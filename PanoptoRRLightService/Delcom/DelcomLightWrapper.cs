using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace RRLightProgram
{
    /// <summary>
    /// This class is in the middle of Delcom light logic and Delcom provided DLL APIs,
    /// and provides the retry and device reconnection underneath.
    /// This finds a Delcom light device in the system. If mutliple devices exist, pick one of them randomly.
    /// Construction fails if no device is found.
    /// Once it's constructed, it continues to try reconnecting when disconnection is found.
    /// </summary>
    internal class DelcomLightWrapper
    {
        #region Variables and Constants

        /// <summary>
        /// Delcom DLL uses 0 as invalid handle.
        /// </summary>
        private const uint InvalidDevcieHandle = 0;

        /// <summary>
        /// Device handle to which we currently access.
        /// This may be updated when reconnection happenes.
        /// </summary>
        private uint deviceHandle = InvalidDevcieHandle;

        /// <summary>
        /// LED states which the class internally holds.
        /// Assume all are on to begin with so that we can turn them off at initialization.
        /// This table itself is also used as a lock to protect from mutliple LED operations.
        /// </summary>
        private Dictionary<DelcomLightColor, DelcomLightState> ligthStates = new Dictionary<DelcomLightColor, DelcomLightState>() {
            { DelcomLightColor.Green, DelcomLightState.On },
            { DelcomLightColor.Red, DelcomLightState.On },
            { DelcomLightColor.Blue, DelcomLightState.On } };

        /// <summary>
        /// Interval before retrying light control call.
        /// </summary>
        private static readonly TimeSpan LightRetryInterval = TimeSpan.FromMilliseconds(500.0);

        /// <summary>
        /// Maximum number of retries when light control call fails.
        /// </summary>
        private const int MaxLightRetries = 5;


        /// <summary>
        /// Interval for the retry when the device is reported as disconnected.
        /// </summary>
        private static readonly TimeSpan DeviceConnectionCheckRetryInterval = TimeSpan.FromSeconds(1.0);
        
        /// <summary>
        /// Interval when retrying to connect the device.
        /// </summary>
        private static readonly TimeSpan DeviceRetryOpenInterval = TimeSpan.FromSeconds(10.0);

        #endregion Variables and Constants

        #region Device setup and cleanup

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <exception cref="ApplicationException">No device is found.</exception>
        public DelcomLightWrapper()
        {
            if (!OpenDevice())
            {
                throw new ApplicationException("No device is found.");
            }

            if (!this.TurnOffAllLights())
            {
                throw new ApplicationException("Failed to initialize light states.");
            }
        }

        /// <summary>
        /// Public method to request cleanup.
        /// </summary>
        public void Close()
        {
            this.CloseDevice();
        }

        /// <summary>
        /// Find and open a Delcom device. this.deviceHandle is set upon success.
        /// </summary>
        /// <returns>true on success.</returns>
        private bool OpenDevice()
        {
            if (this.deviceHandle != InvalidDevcieHandle)
            {
                this.CloseDevice();
            }

            StringBuilder deviceName = new StringBuilder(Delcom.MAXDEVICENAMELEN);

            // Search for the first match USB device, For USB IO Chips use Delcom.USBIODS
            // With Generation 2 HID devices, you can pass a TypeId of 0 to open any Delcom device.
            int findResult = Delcom.DelcomGetNthDevice(Delcom.USBDELVI, 0, deviceName);

            if (findResult == 0)
            {
                Trace.TraceError("Device was not found");
            }
            else
            {
                uint newDeviceHandle = Delcom.DelcomOpenDevice(deviceName, 0);
                if (newDeviceHandle == InvalidDevcieHandle)
                {
                    Trace.TraceError("Device was found, but failed to be connected. device = {0}", deviceName.ToString());
                }
                else
                {
                    this.deviceHandle = newDeviceHandle;

                    // Disable auto confirmation mode where the buzzer will sound when the button is pressed.
                    Delcom.DelcomEnableAutoConfirm(this.deviceHandle, 0);
                }
            }

            return (this.deviceHandle != InvalidDevcieHandle);
        }

        /// <summary>
        /// Close current device. No-op if no device is opened.
        /// </summary>
        private void CloseDevice()
        {
            if (this.deviceHandle != InvalidDevcieHandle)
            {
                Delcom.DelcomCloseDevice(this.deviceHandle);
                this.deviceHandle = InvalidDevcieHandle;
            }
        }

        #endregion

        #region Light control

        /// <summary>
        /// Internal method to set a single color of light.
        /// </summary>
        private bool SetSingleLight(DelcomLightColor color, DelcomLightState newState)
        {
            bool result = false;

            lock (this.ligthStates)
            {
                DelcomLightState currentState;
                if (this.ligthStates.TryGetValue(color, out currentState) && currentState == newState)
                {
                    // This color is already in appropriate state.
                    result = true;
                }
                else
                {
                    for (int i = 0; i < DelcomLightWrapper.MaxLightRetries; i++)
                    {
                        if (Delcom.DelcomLEDControl(this.deviceHandle, (byte)color, (byte)newState) == 0)
                        {
                            this.ligthStates[color] = newState;
                            result = true;
                            break;
                        }
                        else
                        {
                            // Not to log each failure because a) API does not provide any error detail and
                            // b) caller will make an error log if all retries fail.
                            System.Threading.Thread.Sleep(DelcomLightWrapper.LightRetryInterval);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Set the state of a specific color of light.
        /// Internally, this turns off the unspecified light.
        /// </summary>
        public bool SetLight(DelcomLightColor color, DelcomLightState newState)
        {
            if (newState == DelcomLightState.Off)
            {
                throw new ArgumentException("SetLight method cannot be used for turning off");
            }

            bool result = true;
            var colors = new List<DelcomLightColor>(this.ligthStates.Keys);

            foreach (DelcomLightColor targetColor in colors)
            {
                bool singleResult = this.SetSingleLight(
                    targetColor,
                    (targetColor == color) ? newState : DelcomLightState.Off);
                if (!singleResult)
                {
                    Trace.TraceError("SetLight: failed to manipulate {0}", targetColor);
                    result = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Turn off all the lights.
        /// </summary>
        public bool TurnOffAllLights()
        {
            bool result = true;
            var colors = new List<DelcomLightColor>(this.ligthStates.Keys);

            foreach (DelcomLightColor color in colors)
            {
                bool singleResult = this.SetSingleLight(color, DelcomLightState.Off);
                if (!singleResult)
                {
                    Trace.TraceError("TurnOffAllLights: failed to turn off {0}", color);
                    result = false;
                }
            }

            return result;
        }

        #endregion Light control

        #region Button state

        /// <summary>
        /// Get current button state.
        /// This internally checks the connection and try reconnection if needed.
        /// </summary>
        public DelcomButtonState GetButtonState()
        {
            if (!this.DeviceIsConnected())
            {
                Trace.TraceWarning("Delcom light device seems disconnected. Reconnecting.");
                ReopenDevice();
            }

            return (DelcomButtonState)Delcom.DelcomGetButtonStatus(this.deviceHandle);
        }

        /// <summary>
        /// Determine whether the current device is still connected
        /// </summary>
        private bool DeviceIsConnected()
        {
            if (this.deviceHandle == InvalidDevcieHandle)
            {
                return false;
            }

            // If no longer connected we will get a return value of 0 or 255 (manual says 0 but in practice we get 255).
            int deviceVersion = Delcom.DelcomReadDeviceVersion(this.deviceHandle);
            if (deviceVersion == 0 || deviceVersion == 255)
            {
                // This frequently happens (once in a few minutes) even on the healthy system.
                // Retry once before reporting back.
                Thread.Sleep(DelcomLightWrapper.DeviceConnectionCheckRetryInterval);
                deviceVersion = Delcom.DelcomReadDeviceVersion(this.deviceHandle);
                if (deviceVersion == 0 || deviceVersion == 255)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Loop that attempts to reopen a device connection until one is connected.
        /// Block the caller until a device is opened.
        /// Note that this assumes to reconnect to the same device and does not reset
        /// or initialize the states which this class manages (button & LED).
        /// </summary>
        private void ReopenDevice()
        {
            while (!this.OpenDevice())
            {
                Trace.TraceWarning(@"Delcom light device is not connected. Will retry after {0}.", DelcomLightWrapper.DeviceRetryOpenInterval);
                Thread.Sleep(DelcomLightWrapper.DeviceRetryOpenInterval);
            }            
        }

        #endregion Button state
    }

    #region Public enum

    /// <summary>
    /// LED light color values in sync with the DelcomDll.*LED values.
    /// </summary>
    public enum DelcomLightColor : byte
    {
        Green = 0,
        Red = 1,
        Blue = 2,
    }

    /// <summary>
    /// LED light state values in sync with the DelcomDll.LED* values.
    /// </summary>
    public enum DelcomLightState : byte
    {
        Off = 0,
        On = 1,
        Flash = 2,
    }

    /// <summary>
    /// Button state values returned by DelcomDll.DelcomGetButtonStatus.
    /// </summary>
    public enum DelcomButtonState : int
    {
        NotPressed = 0,
        Pressed = 1,
        Unknown = 2,
    }

    #endregion Public enum
}