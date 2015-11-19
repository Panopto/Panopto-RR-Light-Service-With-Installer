using System;
using System.Diagnostics;
using System.IO.Ports;


namespace RRLightProgram
{
    public class SerialComm
    {
        public SerialPort hSerial = null;
        private MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback;
        private bool shouldStop = false;

        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="stateMachineInputCallback">delegate to call when there's an event to report</param>
        public SerialComm(MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback)
        {
            if(String.IsNullOrWhiteSpace(RRLightProgram.Properties.Settings.Default.SerialPortName))
                return;

            try
            {
                hSerial = new SerialPort(
                    RRLightProgram.Properties.Settings.Default.SerialPortName,
                    RRLightProgram.Properties.Settings.Default.SerialPortBaudRate,
                    (Parity)Enum.Parse(typeof(Parity), RRLightProgram.Properties.Settings.Default.SerialPortParity, true),
                    RRLightProgram.Properties.Settings.Default.SerialPortDataBits,
                    (StopBits)Enum.Parse(typeof(StopBits), RRLightProgram.Properties.Settings.Default.SerialPortStopBits, true));

                hSerial.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

                hSerial.Open();

                // remember the delegate so we can invoke when we get input
                this.stateMachineInputCallback = stateMachineInputCallback;

                Trace.TraceInformation(DateTime.Now + ": Opened serial port {0}",
                    RRLightProgram.Properties.Settings.Default.SerialPortName);
                Trace.Flush();
            }
            catch (Exception e)
            {
                Trace.TraceInformation(DateTime.Now + ": Serial: Error opening port {0}: {1}",
                    RRLightProgram.Properties.Settings.Default.SerialPortName,
                    e.Message);
                Trace.Flush();

                this.hSerial = null;
            }
        }

        /// <summary>
        ///     Send a message
        /// </summary>
        /// <param name="str"></param>
        public void SerialOutput(String str)
        {
            if (this.hSerial == null || !this.hSerial.IsOpen)
                return;

            Trace.TraceInformation(DateTime.Now + ": Serial Tx: " + str);
            Trace.Flush();
            this.hSerial.WriteLine(str);
        }

        // Stop the background thread
        public void Stop()
        {
            this.shouldStop = true;
            this.hSerial.Close();
        }
    }
}
