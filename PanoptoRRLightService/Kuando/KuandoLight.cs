using System;
using System.Threading;
using System.Diagnostics;
using Busylight;

namespace RRLightProgram
{
    class KuandoLight : ILightControl
    {
        #region variables

        /// <summary>
        /// Interface that can post events to a state machine.
        /// Does nothing because Kuando Busylight has not input parameters
        /// </summary>
        private IStateMachine stateMachine;

        /// <summary>
        /// Controls light controls
        /// </summary>
        private KuandoLightWrapper wrapper;

        /// <summary>
        /// Event indicating the background threads should stop
        /// </summary>
        private ManualResetEvent stopRequested = new ManualResetEvent(initialState: false);

        /// <summary>
        /// Internal structure to carry the light control request.
        /// </summary>
        struct LightControlRequest
        {
            public LightColor Color;
            public bool Flash;
        };

        /// <summary>
        /// Latest request of light control. Older pending request is disposed by overwrite.
        /// </summary>
        private LightControlRequest? outstandingRequest = null;

        /// <summary>
        /// Lock to access outstandingRequest variable.
        /// </summary>
        private object outstandingRequestLock = new object();

        /// <summary>
        /// Event to be fired when a request is queued.
        /// </summary>
        private ManualResetEvent outstandingRequestExist = new ManualResetEvent(initialState: false);

        /// <summary>
        /// Background thread to process light control requests.
        /// </summary>
        private Thread processLightControlRequestsThread;

        #endregion

        #region Constructor, Initialize

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="stateMachine">The state machine</param>
        public KuandoLight(IStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                throw new ArgumentException("stateMachine cannot be null.");
            }
            this.stateMachine = stateMachine;
        }

        /// <summary>
        /// Start the kuando light service
        /// </summary>
        /// <returns>Whether the service was started successfully</returns>
        public bool Start()
        {
            try
            {
                this.wrapper = new KuandoLightWrapper();
            }
            catch (ApplicationException e)
            {
                Trace.TraceError("Failed to initialize KuandoLightWrapper. {0}", e);
                return false;
            }

            this.processLightControlRequestsThread = new Thread(ProcessLightControlRequestsLoop);
            this.processLightControlRequestsThread.Start();

            Trace.TraceInformation("started kuando light");
            return true;
        }

        /// <summary>
        /// Stop the Kuando light service
        /// </summary>
        public void Stop()
        {
            this.stopRequested.Set();
            this.processLightControlRequestsThread.Join();

            this.wrapper.Close();
        }

        #endregion

        #region Light Control Logic

        /// <summary>
        /// Continue in a thread while the service is running and check for new LightControlRequests
        /// </summary>
        private void ProcessLightControlRequestsLoop()
        {
            while(true)
            {
                int indexFired = WaitHandle.WaitAny(new WaitHandle[] { this.stopRequested, this.outstandingRequestExist });

                if (indexFired == 0)
                {
                    // this.stopRequested fired. Exit the thread.
                    break;
                }

                LightControlRequest? request = null;
                lock (this.outstandingRequestLock)
                {
                    request = this.outstandingRequest;
                    this.outstandingRequest = null;
                    this.outstandingRequestExist.Reset();
                }

                if (!request.HasValue)
                {
                    // Phantom event. Wait next.
                    continue;
                }
                Trace.TraceInformation("ProcessLightControlRequestsLoop processing: color={0}, flash={1}", request.Value.Color, request.Value.Flash);

                BusylightColor color = ConvertColor(request.Value.Color);

                if (request.Value.Flash)
                {
                    wrapper.SetFlash(color);
                }
                else
                {
                    wrapper.SetSolidLight(color);
                }

            }
        }

        /// <summary>
        /// Convert a LightColor to a BusylightColor, which can be recognized by Kuando devices
        /// </summary>
        /// <param name="inputColor">The LightColor input</param>
        /// <returns>The BusyLightColor corresponding to the input</returns>
        private BusylightColor ConvertColor(LightColor inputColor)
        {
            BusylightColor result;

            switch (inputColor)
            {
                case LightColor.Red:
                    result = BusylightColor.Red;
                    break;
                case LightColor.Green:
                    result = BusylightColor.Green;
                    break;
                case LightColor.Yellow:
                    result = BusylightColor.Yellow;
                    break;
                case LightColor.Blue:
                    result = BusylightColor.Blue;
                    break;
                case LightColor.Off:
                    result = BusylightColor.Off;
                    break;
                default:
                    throw new ArgumentException("Unexpected inputColor");
            }

            return result;
        }

        #endregion

        #region ILightControl

        /// <summary>
        /// Create a new request for the Kuando light to flash a given color.
        /// </summary>
        /// <param name="color">The color to flash on the light</param>
        public void SetFlash(LightColor color)
        {
            if (color == LightColor.Off)
            {
                SetSolid(LightColor.Off);
            }
            else
            {
                LightControlRequest request = new LightControlRequest()
                {
                    Color = color,
                    Flash = true
                };
                lock (this.outstandingRequestLock)
                {
                    outstandingRequest = request;
                    outstandingRequestExist.Set();
                    TraceVerbose.Trace("SetFlash({0}): request queued.", color);
                }
            }
        }

        /// <summary>
        /// Set the Kuando light to be a solid color
        /// </summary>
        /// <param name="color">The color to set the light to</param>
        public void SetSolid(LightColor color)
        {
            LightControlRequest request = new LightControlRequest()
            {
                Color = color,
                Flash = false
            };
            lock(this.outstandingRequestLock)
            {
                outstandingRequest = request;
                outstandingRequestExist.Set();
                TraceVerbose.Trace("SetSolid({0}): request queued.", color);
            }
        }

        #endregion
    }
}
