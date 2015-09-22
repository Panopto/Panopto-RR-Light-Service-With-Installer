using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace RRLightProgram
{
    internal class DelcomLightWrapper
    {
        private const int LEDWaitTime = 150;//pretty arbitrary, stops all observed failures and not too noticable a delay
        private const int LEDMaxFailures = 3;//also somewhat arbitrary; should be adjusted after testing
        private static Dictionary<byte,byte> CurrentLEDStates = new Dictionary<byte,byte> //these must stay in sync with DelcomDll.*LED values
            {
                { (byte)0, (byte)LightStates.Off },
                { (byte)1, (byte)LightStates.Off },
                { (byte)2, (byte)LightStates.Off }
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

        public static uint OpenDelcomDevice()
        {
            int Result;
            uint hUSB;
            StringBuilder DeviceName = new StringBuilder(Delcom.MAXDEVICENAMELEN);

            // Search for the first match USB device, For USB IO Chips use Delcom.USBIODS
            // With Generation 2 HID devices, you can pass a TypeId of 0 to open any Delcom device.
            Result = Delcom.DelcomGetNthDevice(Delcom.USBDELVI, 0, DeviceName);

            hUSB = Delcom.DelcomOpenDevice(DeviceName, 0);                      // open the device

            return hUSB;
        }

        public static void CloseDelcomDevice(uint hUSB)
        {
            Delcom.DelcomCloseDevice(hUSB);
        }

        //turns off any LEDs already on or flashing
        public static void DelcomLEDAction(uint hUSB, LightColors color, LightStates action)
        {
            byte dictVal;
            if(CurrentLEDStates.TryGetValue((byte)color, out dictVal) && dictVal == (byte)action)
            {
                //that LED is already in appropriate state
                return;
            }

            for(int i = 0; i < LEDMaxFailures; i++)
            {
                if(Delcom.DelcomLEDControl(hUSB, (byte)color, (byte)action) == 0)
                {
                    CurrentLEDStates[(byte)color] = (byte)action;
                    return;
                }
                else
                {
                    System.Threading.Thread.Sleep(LEDWaitTime);
                }
            }

            throw new OperationCanceledException("Delcom light failed to process LED signals");
        }

        public static void DelcomLEDAllAction(uint hUSB, LightStates action)
        {
            List<byte> lightsRemaining = new List<byte>();

            foreach (KeyValuePair<byte, byte> lightAndState in CurrentLEDStates)
            {
                if (lightAndState.Value != (byte)action)
                {
                    lightsRemaining.Add(lightAndState.Key);
                }
            }

            if (lightsRemaining.Count == 0)
            {
                //all LEDs already in appropriate state
                return;
            }

            for(int i=0; i < LEDMaxFailures; i++)
            {
                List<byte> lightsFailed = new List<byte>();

                foreach (byte remainingLight in lightsRemaining)
                {
                    if (Delcom.DelcomLEDControl(hUSB, remainingLight, (byte)action) == 0)
                    {
                        CurrentLEDStates[(byte)remainingLight] = (byte)action;
                    }
                    else
                    {
                        lightsFailed.Add(remainingLight);
                    }
                }

                if (lightsFailed.Count == 0)
                {
                    return;
                }

                lightsRemaining = lightsFailed;
                System.Threading.Thread.Sleep(LEDWaitTime);
            }


            throw new OperationCanceledException("Delcom light failed to process LED signals");
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
                    Thread.Sleep(1000);
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