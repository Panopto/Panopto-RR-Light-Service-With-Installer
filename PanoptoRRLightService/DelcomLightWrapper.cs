using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace RRLightProgram
{
    internal class DelcomLightWrapper
    {
        /// <summary>
        /// Interval before retrying LED control call.
        /// </summary>
        private static readonly TimeSpan LEDRetryInterval = new TimeSpan(0, 0, 0, 0, 500);

        /// <summary>
        /// Maximum number of retries when LED control call fails.
        /// </summary>
        private const int LEDMaxRetries = 10;

        /// <summary>
        /// Interval when retrying to detect the connected device.
        /// </summary>
        private static readonly TimeSpan DeviceConnectionPollingInterval = new TimeSpan(0, 0, 5);

        // NOTE: These must stay in sync with DelcomDll.*LED values
        // We only include yellow (not blue) because their byte value is the same
        // Assume all are on to begin with so that we can turn them off at initialization.
        private static Dictionary<LightColors, LightStates> CurrentLEDStates = new Dictionary<LightColors, LightStates>
            {
                { LightColors.Green, LightStates.On },
                { LightColors.Red, LightStates.On },
                { LightColors.Yellow, LightStates.On }
            };

        // NOTE: These must stay in sync with the DelcomDll.*LED values
        public enum LightColors : byte
        {
            Green = 0,
            Red = 1,
            Blue = 2,
            Yellow = 2,
        }

        // NOTE: These must stay in sync with the DelcomDll.LED* values
        public enum LightStates : byte
        {
            Off = 0,
            On = 1,
            Flash = 2,
        }

        // NOTE: These map exactly to the values returned by DelcomDll.DelcomGetButtonStatus
        public enum ButtonState : int
        {
            NotPressed = 0,
            Pressed = 1,
            Unknown = 2,
        }

        /// <summary>
        /// Return the handle value to represent the connected device.
        /// </summary>
        /// <returns>Non-0 for the opened device. 0 if any device is not opened.</returns>
        public static uint OpenDelcomDevice()
        {
            int found;
            uint hUSB = 0;
            StringBuilder DeviceName = new StringBuilder(Delcom.MAXDEVICENAMELEN);

            // Search for the first match USB device, For USB IO Chips use Delcom.USBIODS
            // With Generation 2 HID devices, you can pass a TypeId of 0 to open any Delcom device.
            found = Delcom.DelcomGetNthDevice(Delcom.USBDELVI, 0, DeviceName);

            if (found > 0)
            {
                hUSB = Delcom.DelcomOpenDevice(DeviceName, 0);
            }

            return hUSB;
        }

        public static void CloseDelcomDevice(uint hUSB)
        {
            Delcom.DelcomCloseDevice(hUSB);
        }

        public static bool DelcomLEDAction(uint hUSB, LightColors color, LightStates action)
        {
            bool success = false;

            //lock here so multiple threads don't try to toggle LEDs at once
            //(though presently only one instance of the program works with the light at a time anyway)
            lock (DelcomLightWrapper.CurrentLEDStates)
            {
                LightStates dictVal;
                if (DelcomLightWrapper.CurrentLEDStates.TryGetValue(color, out dictVal) && dictVal == action)
                {
                    //that LED is already in appropriate state
                    success = true;
                }
                else
                {
                    for (int i = 0; i < DelcomLightWrapper.LEDMaxRetries; i++)
                    {
                        if (Delcom.DelcomLEDControl(hUSB, (byte)color, (byte)action) == 0)
                        {
                            DelcomLightWrapper.CurrentLEDStates[color] = action;
                            success = true;
                            break;
                        }
                        else
                        {
                            // Not to log each failure because a) API does not provide any error detail and
                            // b) caller will make an error log if all retries fail.
                            System.Threading.Thread.Sleep(DelcomLightWrapper.LEDRetryInterval);
                        }
                    }
                }
            }

            return success;
        }

        public static bool DelcomLEDAllAction(uint hUSB, LightStates action)
        {
            bool success = false;

            List<LightColors> lightsRemaining = new List<LightColors>();

            //lock here so multiple threads don't try to toggle LEDs at once
            //(though presently only one instance of the program works with the light at a time anyway)
            lock (DelcomLightWrapper.CurrentLEDStates)
            {
                foreach (KeyValuePair<LightColors, LightStates> lightAndState in DelcomLightWrapper.CurrentLEDStates)
                {
                    if (lightAndState.Value != action)
                    {
                        lightsRemaining.Add(lightAndState.Key);
                    }
                }

                if (lightsRemaining.Count == 0)
                {
                    //all LEDs already in appropriate state
                    success = true;
                }
                else
                {
                    for (int i = 0; i < DelcomLightWrapper.LEDMaxRetries; i++)
                    {
                        List<LightColors> lightsFailed = new List<LightColors>();

                        foreach (LightColors remainingLight in lightsRemaining)
                        {
                            if (Delcom.DelcomLEDControl(hUSB, (byte)remainingLight, (byte)action) == 0)
                            {
                                DelcomLightWrapper.CurrentLEDStates[remainingLight] = action;
                            }
                            else
                            {
                                // Not to log each failure because a) API does not provide any error detail and
                                // b) caller will make an error log if all retries fail.
                                lightsFailed.Add(remainingLight);
                            }
                        }

                        if (lightsFailed.Count == 0)
                        {
                            success = true;
                            break;
                        }

                        lightsRemaining = lightsFailed;
                        System.Threading.Thread.Sleep(DelcomLightWrapper.LEDRetryInterval);
                    }
                }
            }

            return success;
        }

        public static ButtonState DelcomGetButtonStatus(uint hUSB)
        {
            return (ButtonState)Delcom.DelcomGetButtonStatus(hUSB);
        }

        /// <summary>
        /// Method used to determine whether a specific device is still connected
        /// </summary>
        /// <param name="hUSB"></param>
        /// <returns></returns>
        public static bool isButtonConnected(uint hUSB)
        {
            int deviceVersion = Delcom.DelcomReadDeviceVersion(hUSB);
            
            //If no longer connected we will get a return value of 0 or 255 (manual says 0 but in practice we get 255)
            if (deviceVersion == 0 || deviceVersion == 255)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Loop that attempts to open a device connection until one is connected. Replaces old
        /// device id with new one.
        /// </summary>
        public static uint TryOpeningDelcomDevice()
        {
            // Initialize the light wrapper
            uint hUSB = 0;
            bool deviceOpened = false;
            
            //While no light has been found, wait for a connection
            while (deviceOpened == false)
            {
                hUSB = DelcomLightWrapper.OpenDelcomDevice();
                if (hUSB == 0)
                {
                    //If no light found, wait for a second and then try to open again.
                    Trace.TraceWarning(@"Delcom device is not found. Sleep {0} and retry.", DelcomLightWrapper.DeviceConnectionPollingInterval);
                    Thread.Sleep(DelcomLightWrapper.DeviceConnectionPollingInterval);
                }
                else
                {
                    deviceOpened = true;
                }
            }            
            
            return hUSB;
        }

    }
}