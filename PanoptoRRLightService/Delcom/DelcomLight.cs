using System;
using System.Threading;
using System.Diagnostics;

namespace RRLightProgram
{
    public class DelcomLight : ILightControl
    {
        #region Variables

        /// <summary>
        /// 
        /// </summary>
        private DelcomLightWrapper wrapper;

        /// <summary>
        /// Interface to post input events to the state machine.
        /// </summary>
        private IStateMachine stateMachine;

        /// <summary>
        /// Event to indicate that back ground threads should stop.
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

        /// <summary>
        /// Background thread to handle button state.
        /// </summary>
        private Thread handleButtonThread;

        /// <summary>
        /// Button state is somehow fragile. We recognize the button state change only after
        /// the same state is reported this number of times.
        /// </summary>
        private const int ButtonRecognitionThreshold = 3;

        #endregion

        #region Constructor, Initialize, and Cleanup

        /// <summary>
        /// Constructor
        /// </summary>
        public DelcomLight(IStateMachine stateMachine)
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
                this.wrapper = new DelcomLightWrapper();
            }
            catch (ApplicationException e)
            {
                Trace.TraceError("Failed to initialize DelcomLight wrapper. {0}", e);
                return false;
            }

            // Start a background thread to process light control requests.
            this.processLightControlRequestsThread = new Thread(ProcessLightControlRequestsLoop);
            this.processLightControlRequestsThread.Start();

            // Start a background thread to monitor and process the button state.
            this.handleButtonThread = new Thread(HandleButtonLoop);
            this.handleButtonThread.Start();

            return true;
        }

        /// <summary>
        /// Stop the background threads and close the device.
        /// </summary>
        public void Stop()
        {
            this.stopRequested.Set();
            this.processLightControlRequestsThread.Join();
            this.handleButtonThread.Join();

            this.wrapper.Close();
        }

        #endregion

        #region ILightControl

        public void SetSolid(LightColor color)
        {
            LightControlRequest request = new LightControlRequest()
            {
                Color = color,
                Flash = false
            };
            lock (this.outstandingRequestLock)
            {
                outstandingRequest = request;
                outstandingRequestExist.Set();
                TraceVerbose.Trace("SetSolid({0}): request queued.", color);
            }
        }

        public void SetFlash(LightColor color)
        {
            if (color == LightColor.Off)
            {
                throw new ArgumentException("LightColor.Off is invalid");
            }
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

        #endregion

        #region Light control logic

        /// <summary>
        /// A lLoop in background thread. Change the color of the light and flash.
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

                if (request.Value.Color == LightColor.Off)
                {
                    if (!this.wrapper.TurnOffAllLights())
                    {
                        Trace.TraceError("ProcessLightControlRequestsLoop failure: off");
                    }
                    else
                    {
                        TraceVerbose.Trace("ProcessLightControlRequestsLoop success: off");
                    }
                }
                else
                {
                    DelcomLightColor color = ConvertColor(request.Value.Color);
                    DelcomLightState state = request.Value.Flash ? DelcomLightState.Flash : DelcomLightState.On;
                    if (!wrapper.SetLight(color, state))
                    {
                        Trace.TraceError("ProcessLightControlRequestsLoop failure: color={0}, state={1}", color, state);
                    }
                    else
                    {
                        TraceVerbose.Trace("ProcessLightControlRequestsLoop success: color={0}, state={1}", color, state);
                    }
                }
            }
        }

        /// <summary>
        /// Helper to convert our 'public-facing' color enum to the Delcom light.
        /// Delcom light has two versions, one supports yellow nad the other suports blue.
        /// Although Delcom document is vague, distributed DelcomDLL.h has only a single entry BLUELED = 2.
        /// We observe that each device picks either blue or yellow for this value.
        /// </summary>
        /// <param name="inputColor"></param>
        /// <returns></returns>
        private DelcomLightColor ConvertColor(LightColor inputColor)
        {
            DelcomLightColor result;

            switch (inputColor)
            {
                case LightColor.Red:
                    result = DelcomLightColor.Red;
                    break;

                case LightColor.Green:
                    result = DelcomLightColor.Green;
                    break;

                case LightColor.Yellow:
                    result = DelcomLightColor.Blue;
                    break;

                default:
                    throw new ArgumentException("Unexpected inputColor");
            }

            return result;
        }

        #endregion 

        #region Button handler

        /// <summary>
        /// A loop in background thread to monitor the button state and dispatch events to the state machine.
        /// Since Delcom light does not provide the event, this logic needs to use 
        /// </summary>
        private void HandleButtonLoop()
        {
            DelcomButtonState currentState = DelcomButtonState.NotPressed;
            int countButtonUpEvent = 0;
            int countButtonDownEvent = 0;
            bool buttonHeld = false;

            //The time when the button was last in a down state.
            DateTime lastButtonDownTime = DateTime.MinValue;

            //The time when the button was last in an up state.
            DateTime lastButtonUpTime = DateTime.MinValue;

            // Loop endlessly until we're asked to stop
            while (!this.stopRequested.WaitOne(0))
            {
                // We do not examine the button state at all for small duraiton (300ms default) after the button is up
                // last time in order to avoid two sequential 'pressed' events are fired unintentionally.
                if ((DateTime.UtcNow - lastButtonUpTime) > Properties.Settings.Default.DelcomButtonIgnoreAfterButtonUp)
                {
                    // Get the current state of the button
                    DelcomButtonState newState = this.wrapper.GetButtonState();

                    if (newState == DelcomButtonState.Unknown)
                    {
                        throw new NotImplementedException();
                    }
                    else if (newState != currentState)
                    {
                        // The state has changed, so we need to handle it

                        if (newState == DelcomButtonState.NotPressed)
                        {
                            // We were previously in a pressed state, but the button on the light is flaky, so we need
                            // to make sure the button has really been released, so we'll wait a few iterations.
                            countButtonUpEvent++;

                            if (countButtonUpEvent >= DelcomLight.ButtonRecognitionThreshold)
                            {
                                // Only remember the currentstate as changed if we're exceeding threshold
                                currentState = newState;

                                // Button just released so reset timer
                                countButtonUpEvent = 0;

                                // Fire a button up event.
                                // This is independent from button pressed or button held.
                                this.stateMachine.PostInput(Input.ButtonUp);

                                // If a button held event was already fired, we don't want to fire a button pressed event.
                                if (!buttonHeld)
                                {
                                    this.stateMachine.PostInput(Input.ButtonPressed);
                                }

                                lastButtonUpTime = DateTime.UtcNow;

                                // Reset button held, as it is no longer being held if it was before
                                buttonHeld = false;
                            }
                        }
                        else if (newState == DelcomButtonState.Pressed)
                        {
                            // We were previously in a released state, but the button on the light is flaky, so we need
                            // to make sure the button has really been pressed, so we'll wait a few iterations.
                            countButtonDownEvent++;

                            if (countButtonDownEvent >= DelcomLight.ButtonRecognitionThreshold)
                            {
                                // The button has just been pressed

                                // Only remember the current state as changed if we're exceeding threshold
                                currentState = newState;

                                // Button just pressed so reset timer
                                countButtonDownEvent = 0;

                                // Fire a button down event.
                                // This is independent from button pressed or button held.
                                this.stateMachine.PostInput(Input.ButtonDown);

                                // Set last button down time to current time, as button was just pressed down
                                lastButtonDownTime = DateTime.UtcNow;
                            }
                        }
                    }
                    else if (newState == DelcomButtonState.Pressed) // && oldState == ButtonState.Pressed
                    {
                        // Button has been held, check if hold is greater than threshold
                        TimeSpan holdDuration = DateTime.UtcNow - lastButtonDownTime;

                        // Reset iterations since last release
                        countButtonUpEvent = 0;

                        // If button held event has already been fired, we don't want to fire again until the button has been released
                        if (!buttonHeld)
                        {
                            // If hold duration is greater than threshold we should fire a button held event
                            // Note that due to asynchronous operation and intentional recognition delay (ButtonRecognitionThreshold),
                            // actual hold threshold may be longer than exactly specified duration here.
                            if (holdDuration > Properties.Settings.Default.DelcomButtonHoldThreshold)
                            {
                                this.stateMachine.PostInput(Input.ButtonHeld);
                                buttonHeld = true;
                            }
                        }
                    }
                    else if (newState == DelcomButtonState.NotPressed) // && oldState == ButtonState.NotPressed
                    {
                        // Reset iterations since last press
                        countButtonDownEvent = 0;
                    }
                }

                System.Threading.Thread.Sleep(Properties.Settings.Default.DelcomButtonPollingInterval);
            }
        }

        #endregion
    }
}