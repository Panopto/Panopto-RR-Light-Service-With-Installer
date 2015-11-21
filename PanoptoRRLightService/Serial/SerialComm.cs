using System;
using System.Diagnostics;
using System.IO.Ports;

namespace RRLightProgram
{
    public class SerialComm : IConsole
    {
        #region Variables

        /// <summary>
        /// Our Serial Port
        /// </summary>
        private SerialPort portObj;

        /// <summary>
        /// Interface to post input events to the state machine.
        /// </summary>
        private IStateMachine stateMachine;

        #endregion

        #region Constructor, Initialize, and Cleanup

        /// <summary>
        /// Constructor
        /// </summary>
        public SerialComm(IStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                throw new ArgumentException("stateMachine cannot be null.");
            }

            this.stateMachine = stateMachine;

        }

        /// <summary>
        /// Initialize the device and start background threads.
        /// </summary>
        /// <returns>true on success, false on failure.</returns>
        public bool Start()
        {
            try
            {
                portObj = new SerialPort(
                    Properties.Settings.Default.SerialPortName,
                    Properties.Settings.Default.SerialPortBaudRate,
                    (Parity)Enum.Parse(typeof(Parity), Properties.Settings.Default.SerialPortParity, true),
                    Properties.Settings.Default.SerialPortDataBits,
                    (StopBits)Enum.Parse(typeof(StopBits), Properties.Settings.Default.SerialPortStopBits, true));

                portObj.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

                portObj.Open();

                Trace.TraceInformation(DateTime.Now + ": Opened serial port {0}",
                    Properties.Settings.Default.SerialPortName);
            }
            catch (ApplicationException e)
            {
                Trace.TraceError("Failed to initialize Serial port {0}: {1}",
                    Properties.Settings.Default.SerialPortName, e);

                this.portObj = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Close the device.
        /// </summary>
        public void Stop()
        {
            this.portObj.Close();
        }

        #endregion

        #region IConsole

        public void Output(String str)
        {
            Trace.TraceInformation(DateTime.Now + ": Serial Tx: " + str);
            this.portObj.WriteLine(str);
        }

        #endregion

        #region Serial input handler

        /// <summary>
        /// Handle events sent by our portObj
        /// </summary>
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;

            while (sp.IsOpen && sp.BytesToRead > 0)
            {
                string inputString = sp.ReadLine().TrimEnd('\r');
                Command inputCommand;

                Trace.TraceInformation(DateTime.Now + ": Serial Rx: " + inputString);

                //Fire the command event.
                if (Enum.TryParse(inputString, true, out inputCommand))
                {
                    Input inputSMInput;

                    // State machine input?
                    if (Enum.TryParse("Command" + inputString, true, out inputSMInput))
                    {
                        this.stateMachine.PostInput(inputSMInput);
                    }
                    else
                    {
                        Trace.TraceError(DateTime.Now + ": Serial: Unhandled command '{0}'", inputString);
                        this.Output("Error: Unhandled console command: " + inputString);
                    }
                }
                else
                {
                    Trace.TraceInformation(DateTime.Now + ": Serial: Command '{0}' not found", inputString);
                    this.Output("Serial-Error: Command not found: " + inputString);
                }
            }
        }

        #endregion
    }
}
