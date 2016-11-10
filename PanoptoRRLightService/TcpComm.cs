using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;
using tcpServer;

namespace RRLightProgram
{
    public class TcpComm
    {
        public TcpServer server = new TcpServer();

        private IStateMachine stateMachine;

        /// <summary>
        ///     Constructor. Open the server on port given in settings.
        /// </summary>
        /// <param name="stateMachine">interface to the state machine</param>
        public TcpComm(IStateMachine stateMachine)
        {
            // check the settings to make sure we've be asked to run the tcp server and validate the port number. If not, return.
            if ((!Properties.Settings.Default.TcpServer) && (Properties.Settings.Default.TcpServerPort > 0))
                return;

             try
            {
                server.Port = RRLightProgram.Properties.Settings.Default.TcpServerPort;
                server.IdleTime = 50;
                server.IsOpen = false;
                server.MaxCallbackThreads = 100;
                server.MaxSendAttempts = 3;
                server.VerifyConnectionInterval = 0;
                server.OnDataAvailable += new tcpServerConnectionChanged(server_OnDataAvailable);
                server.OnConnect += new tcpServerConnectionChanged(server_OnConnect);

                server.Open();

                this.stateMachine = stateMachine;

                Trace.TraceInformation(DateTime.Now + ": TCP - Starting TCP Server on port {0}", RRLightProgram.Properties.Settings.Default.TcpServerPort);
                Trace.Flush();

            }
            catch (Exception e)
            {
                Trace.TraceInformation(DateTime.Now + ": TCP - Error starting TCP Server: {0}", e.Message);
                Trace.Flush();

                server = null;
            }

        }

        /// <summary>
        ///     Callback when a client connects
        /// </summary>
        /// <param name="connection">The TCP connection handler</param>
        /// 
        private void server_OnConnect(tcpServer.TcpServerConnection connection)
        {
            // Tell us who has connected

            Trace.TraceInformation(DateTime.Now + ": TCP - New client connection from {0}", connection.Socket.Client.RemoteEndPoint);
            Trace.Flush();
            
        }

        /// <summary>
        ///     Callback when a when data is available from the connection. 
        ///     Parses response and submits an input event to the state machine.
        /// </summary>
        /// <param name="connection">The TCP connection handler</param>
        /// 
        private void server_OnDataAvailable(tcpServer.TcpServerConnection connection)
        {
            var TCPSMCommands = new Dictionary<string, Input >(StringComparer.OrdinalIgnoreCase)
            {
                { "START", Input.CommandStart },
                { "STOP", Input.CommandStop },
                { "PAUSE", Input.CommandPause },
                { "RESUME", Input.CommandResume },

            };

            byte[] data = readStream(connection.Socket);

            if (data != null)
            {
                // Remove line endings
                string dataStr = Encoding.ASCII.GetString(data).TrimEnd('\n','\r');

                Input inputCommand = Input.None;

                Trace.TraceInformation(DateTime.Now + ": TCP - Rx: " + dataStr);
                Trace.Flush();

                //Fire the command event.
                if (this.stateMachine != null && TCPSMCommands.TryGetValue(dataStr, out inputCommand))
                {

                    this.stateMachine.PostInput(inputCommand);
                    
                }
                else
                {
                    Trace.TraceInformation(DateTime.Now + ": TCP - Command '{0}' not found", dataStr);
                    Trace.Flush();

                    TcpSend("TCP-Error: Command not found: " + dataStr);
                }


            }

        }

        /// <summary>
        ///     Read the data stream from the connection. 
        /// </summary>
        /// <param name="client">The TCP connection handler</param>
        /// <returns>data if available</returns>
        /// 
        protected byte[] readStream(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            if (stream.DataAvailable)
            {
                byte[] data = new byte[client.Available];

                int bytesRead = 0;
                try
                {
                    bytesRead = stream.Read(data, 0, data.Length);
                }
                catch (IOException)
                {
                }

                if (bytesRead < data.Length)
                {
                    byte[] lastData = data;
                    data = new byte[bytesRead];
                    Array.ConstrainedCopy(lastData, 0, data, 0, bytesRead);
                }
                return data;
            }
            return null;
        }

        /// <summary>
        ///     Public interface to send data to the client. 
        /// </summary>
        /// <param name="str">The data to send</param>
        /// 
        public void TcpSend(String str)
        {
            if (server == null || !server.IsOpen)
                return;

            Trace.TraceInformation(DateTime.Now + ": TCP - Tx: " + str);
            Trace.Flush();

            server.Send(str + "\n");


        }


        /// <summary>
        ///     Close the server on a request to stop. 
        /// </summary>
        /// 
        public void Stop()
        {
            server.Close();
        }


    }
}
