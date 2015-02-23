using System.Text;
using System.Threading;

namespace RRLightProgram
{
    internal class DelcomLightWrapper
    {
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

            // Serach for the first match USB device, For USB IO Chips use Delcom.USBIODS
            // With Generation 2 HID devices, you can pass a TypeId of 0 to open any Delcom device.
            Result = Delcom.DelcomGetNthDevice(Delcom.USBDELVI, 0, DeviceName);

            hUSB = Delcom.DelcomOpenDevice(DeviceName, 0);                      // open the device
            return hUSB;
        }

        public static void CloseDelcomDevice(uint hUSB)
        {
            Delcom.DelcomCloseDevice(hUSB);
        }

        public static void DelcomLEDOn(uint hUSB, LightColors color, LightStates action)
        {
            DelcomLEDOffAction(hUSB);

            Delcom.DelcomLEDControl(hUSB, (byte)color, (byte)action);
        }

        public static void DelcomLEDAllOn(uint hUSB, LightStates action)
        {
            DelcomLEDOffAction(hUSB);
            Delcom.DelcomLEDControl(hUSB, Delcom.REDLED, (byte)action);
            Delcom.DelcomLEDControl(hUSB, Delcom.GREENLED, (byte)action);
            Delcom.DelcomLEDControl(hUSB, Delcom.YELLOWLED, (byte)action);
            Delcom.DelcomLEDControl(hUSB, Delcom.BLUELED, (byte)action);
        }

        public static void DelcomLEDOffAction(uint hUSB)
        {
            Delcom.DelcomLEDControl(hUSB, Delcom.REDLED, (byte)LightStates.Off);
            Delcom.DelcomLEDControl(hUSB, Delcom.GREENLED, (byte)LightStates.Off);
            Delcom.DelcomLEDControl(hUSB, Delcom.YELLOWLED, (byte)LightStates.Off);
            Delcom.DelcomLEDControl(hUSB, Delcom.BLUELED, (byte)LightStates.Off);
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