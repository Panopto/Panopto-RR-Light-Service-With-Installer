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

        /// <summary>
        /// Controller of the remote recorder.
        /// This is used to get recording info from the remote controller.
        /// </summary>
        private RemoteRecorderSync remoteRecorder;

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
        public bool Start(RemoteRecorderSync remoteRecorder)
        {
            if (remoteRecorder == null)
            {
                throw new ArgumentException("remoteRecorder cannot be null.");
            }

            this.remoteRecorder = remoteRecorder;

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
                    switch (inputCommand)
                    {
                        case Command.Start:
                            this.stateMachine.PostInput(Input.CommandStart);
                            break;
                        case Command.Stop:
                            this.stateMachine.PostInput(Input.CommandStop);
                            break;
                        case Command.Pause:
                            this.stateMachine.PostInput(Input.CommandPause);
                            break;
                        case Command.Resume:
                            this.stateMachine.PostInput(Input.CommandResume);
                            break;
                        case Command.Extend:
                            this.stateMachine.PostInput(Input.CommandExtend);
                            break;
                        case Command.Status:
                            this.OutputStatus(inputString);
                            break;
                        default:
                            Trace.TraceError(DateTime.Now + ": Serial: Unhandled command '{0}'", inputString);
                            this.Output("Error: Unhandled console command: " + inputString);
                            break;
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

        private void OutputStatus(string inputCommand)
        {
            var currentRecording = this.remoteRecorder.GetCurrentRecording();
            var nextRecording = this.remoteRecorder.GetNextRecording();

            this.Output("Recorder-Status: " + this.stateMachine.GetCurrentState());

            if (currentRecording != null)
            {
                this.Output("CurrentRecording-Id: " + currentRecording.Id);
                this.Output("CurrentRecording-Name: " + currentRecording.Name);
                this.Output("CurrentRecording-StartTime: " + currentRecording.StartTime.ToLocalTime());
                this.Output("CurrentRecording-EndTime: " + currentRecording.EndTime.ToLocalTime());
                this.Output("CurrentRecording-MinutesUntilStartTime: " +
                    (int)(currentRecording.StartTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
                this.Output("CurrentRecording-MinutesUntilEndTime: " +
                    (int)(currentRecording.EndTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
            }
            if (nextRecording != null)
            {
                this.Output("NextRecording-Id: " + nextRecording.Id);
                this.Output("NextRecording-Name: " + nextRecording.Name);
                this.Output("NextRecording-StartTime: " + nextRecording.StartTime.ToLocalTime());
                this.Output("NextRecording-EndTime: " + nextRecording.EndTime.ToLocalTime());
                this.Output("NextRecording-MinutesUntilStartTime: " +
                    (int)(nextRecording.StartTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
                this.Output("NextRecording-MinutesUntilEndTime: " +
                    (int)(nextRecording.EndTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
            }
        }

    }
}
