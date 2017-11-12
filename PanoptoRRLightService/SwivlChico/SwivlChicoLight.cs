using System;
using System.Threading;
using System.Diagnostics;

namespace RRLightProgram
{
    public class SwivlChicoLight : ILightControl
    {
        #region Variables

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

        #endregion

        #region Constructor, Initialize, and Cleanup

        /// <summary>
        /// Constructor
        /// </summary>
        public SwivlChicoLight(IStateMachine stateMachine)
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
            SwivlChico.Chico_SetColor((byte)SwivlChicoLightColor.COLOR_PERMANENT_BLACK);

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

            SwivlChico.Chico_SetColor((byte)SwivlChicoLightColor.COLOR_PERMANENT_BLACK);
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

                SwivlChicoLightColor color = ConvertColor(request.Value.Color, request.Value.Flash);
                SwivlChico.Chico_SetColor((byte)color);
            }
        }

        /// <summary>
        /// Helper to convert our 'public-facing' color enum to the SwivlChico light.
        /// </summary>
        /// <param name="inputColor"></param>
        /// <param name="flash"></param>
        /// <returns></returns>
        private SwivlChicoLightColor ConvertColor(LightColor inputColor, bool flash)
        {
            SwivlChicoLightColor result;

            if (flash)
            {
                switch (inputColor)
                {
                    case LightColor.Red:
                        result = SwivlChicoLightColor.COLOR_MEDIUM_FLASH_RED;
                        break;

                    case LightColor.Green:
                        result = SwivlChicoLightColor.COLOR_MEDIUM_FLASH_GREEN;
                        break;

                    case LightColor.Yellow:
                        result = SwivlChicoLightColor.COLOR_MEDIUM_FLASH_YELLOW;
                        break;

                    default:
                        throw new ArgumentException("Unexpected inputColor&flash");
                }
            }
            else
            {
                switch (inputColor)
                {
                    case LightColor.Off:
                        result = SwivlChicoLightColor.COLOR_PERMANENT_BLACK;
                        break;

                    case LightColor.Red:
                        result = SwivlChicoLightColor.COLOR_PERMANENT_RED;
                        break;

                    case LightColor.Green:
                        result = SwivlChicoLightColor.COLOR_PERMANENT_GREEN;
                        break;

                    case LightColor.Yellow:
                        result = SwivlChicoLightColor.COLOR_PERMANENT_YELLOW;
                        break;

                    default:
                        throw new ArgumentException("Unexpected inputColor&flash");
                }
            }

            return result;
        }

        #endregion 

        #region Button handler

        /// <summary>
        /// A loop in background thread to monitor the button state and dispatch events to the state machine.
        /// Since SwivlChico light does not provide the event, this logic needs to use 
        /// </summary>
        private void HandleButtonLoop()
        {
            SwivlChicoButtonState currentState = SwivlChicoButtonState.NotPressed;
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
                    // We ignore the Unknown state: change to Unpressed
                    SwivlChicoButtonState newState = ((SwivlChicoButtonState)SwivlChico.Chico_GetRecordButtonState() == SwivlChicoButtonState.Pressed) ?
                        SwivlChicoButtonState.Pressed : SwivlChicoButtonState.NotPressed;

                    if (newState == SwivlChicoButtonState.Unknown)
                    {
                        throw new NotImplementedException();
                    }
                    else if (newState != currentState)
                    {
                        // The state has changed, so we need to handle it
                        currentState = newState;

                        if (newState == SwivlChicoButtonState.NotPressed)
                        {
                            // Fire a button up event.
                            // This is independent from button pressed or button held.
                            this.stateMachine.PostInput(Input.ButtonUp);

                            // If a button held event was already fired, we don't want to fire a button pressed event.
                            if (!buttonHeld)
                            {
                                this.stateMachine.PostInput(Input.ButtonPressed);
                            }

                            // Set last button up time to current time, as button was just released
                            lastButtonUpTime = DateTime.UtcNow;

                            // Reset button held, as it is no longer being held if it was before
                            buttonHeld = false;
                        }
                        else if (newState == SwivlChicoButtonState.Pressed)
                        {
                            // Fire a button down event.
                            // This is independent from button pressed or button held.
                            this.stateMachine.PostInput(Input.ButtonDown);

                            // Set last button down time to current time, as button was just pressed down
                            lastButtonDownTime = DateTime.UtcNow;
                        }
                    }
                    else if (newState == SwivlChicoButtonState.Pressed) // && oldState == ButtonState.Pressed
                    {
                        // Button has been held, check if hold is greater than threshold
                        TimeSpan holdDuration = DateTime.UtcNow - lastButtonDownTime;

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
                }

                System.Threading.Thread.Sleep(Properties.Settings.Default.SwivlChicoButtonPollingInterval);
            }
        }

        #endregion
    }


    #region Public enum

    /// <summary>
    /// LED light color values in sync with the chicntrl.dll.Chico_SetColor values.
    /// </summary>
    public enum SwivlChicoLightColor : byte
    {
        COLOR_PERMANENT_BLACK = 0,
        COLOR_PERMANENT_RED = 1,
        COLOR_PERMANENT_GREEN = 2,
        COLOR_PERMANENT_YELLOW = 3,
        COLOR_ONE_BLINK_RED = 4,
        COLOR_ONE_BLINK_GREEN = 5,
        COLOR_ONE_BLINK_YELLOW = 6,
        COLOR_FAST_FLASH_RED = 7,
        COLOR_FAST_FLASH_GREEN = 8,
        COLOR_FAST_FLASH_YELLOW = 9,
        COLOR_MEDIUM_FLASH_RED = 10,
        COLOR_MEDIUM_FLASH_GREEN = 11,
        COLOR_MEDIUM_FLASH_YELLOW = 12,
        COLOR_SLOW_FLASH_RED = 13,
        COLOR_SLOW_FLASH_GREEN = 14,
        COLOR_SLOW_FLASH_YELLOW = 15,
        COLOR_ONCE_FLASH_RED = 16,
        COLOR_ONCE_FLASH_GREEN = 17,
        COLOR_ONCE_FLASH_YELLOW = 18,
        COLOR_TWICE_FLASH_RED = 19,
        COLOR_TWICE_FLASH_GREEN = 20,
        COLOR_TWICE_FLASH_YELLOW = 21,
    }

    /// <summary>
    /// Button state values returned by chicntrl.dll.SwivlChicoGetButtonStatus.
    /// </summary>
    public enum SwivlChicoButtonState : int
    {
        Unknown = -1,
        NotPressed = 0,
        Pressed = 1,
    }

    #endregion Public enum

}