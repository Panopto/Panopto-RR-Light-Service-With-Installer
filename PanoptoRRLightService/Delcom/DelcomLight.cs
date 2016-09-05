using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LightService = RRLightProgram.Program;
using ButtonState = RRLightProgram.DelcomLightWrapper.ButtonState;
using System.Diagnostics;


namespace RRLightProgram
{
    public class DelcomLight : ILightControl
    {
        #region Variables

        /// <summary>
        /// 
        /// </summary>
        public uint deviceHandle;

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

            this.deviceHandle = DelcomLightWrapper.TryOpeningDelcomDevice();

            Delcom.DelcomEnableAutoConfirm(this.deviceHandle, 0);
            // Make sure we always start turned off
            DelcomLightWrapper.DelcomLEDAllAction(this.deviceHandle, DelcomLightWrapper.LightStates.Off);

            // Start a background thread to process light control requests.
            this.processLightControlRequestsThread = new Thread(ProcessLightControlRequestsLoop);
            this.processLightControlRequestsThread.Start();

            // Start a background thread to monitor and process the button state.
            this.handleButtonThread = new Thread(HandleButtonLoop);
            this.handleButtonThread.Start();
        }

        /// <summary>
        /// Initialize the device and start background threads.
        /// </summary>
        /// <returns>true on success, false on failure.</returns>
        public bool Start()
        {
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

            DelcomLightWrapper.CloseDelcomDevice(this.deviceHandle);
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
            }
            TraceVerbose.Trace("SetSolid({0}): request queued.", color);
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
            }
            TraceVerbose.Trace("SetFlash({0}): request queued.", color);
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
                    if (!DelcomLightWrapper.DelcomLEDAllAction(this.deviceHandle, DelcomLightWrapper.LightStates.Off))
                    {
                        Trace.TraceError("DelcomLEDAllAction: failed to turn off.");
                    }
                    else
                    {
                        TraceVerbose.Trace("ProcessLightControlRequestsLoop complete: off");
                    }
                }
                else
                {
                    DelcomLightWrapper.LightColors color = ConvertColor(request.Value.Color);
                    DelcomLightWrapper.LightStates action = request.Value.Flash ?
                        DelcomLightWrapper.LightStates.Flash :
                        DelcomLightWrapper.LightStates.On;
                    if (!DelcomLightWrapper.DelcomLEDAction(this.deviceHandle, color, action))
                    {
                        Trace.TraceError("DelcomLEDAllAction: failed to set {0} / {1}",
                            Enum.GetName(typeof(DelcomLightWrapper.LightColors), color),
                            Enum.GetName(typeof(DelcomLightWrapper.LightStates), action));
                    }
                    else
                    {
                        TraceVerbose.Trace("ProcessLightControlRequestsLoop complete: color={0}, flash={1}", request.Value.Color, request.Value.Flash);
                    }
                }
            }
        }

        /// <summary>
        /// Helper to convert our 'public-facing' color enum to the delcom light.
        /// Given that DelcomLightWrapper.LightColors.Yellow == DelcomLightWrapper.LightColors.Blue
        /// (the byte we send to the light indicating which LED we want to change is the same whether
        /// it supports yellow or blue), this makes no difference at present.
        /// </summary>
        /// <param name="inputColor"></param>
        /// <returns></returns>
        private DelcomLightWrapper.LightColors ConvertColor(LightColor inputColor)
        {
            DelcomLightWrapper.LightColors result = DelcomLightWrapper.LightColors.Red;

            // Make this a configurable value since delcom lights come in both RGY (the default) and RGB, and
            // we want to be able to work with both.
            bool lightSupportsYellow = RRLightProgram.Properties.Settings.Default.DelcomLightSupportsYellow;

            switch (inputColor)
            {
                case LightColor.Red:
                    result = DelcomLightWrapper.LightColors.Red;
                    break;

                case LightColor.Green:
                    result = DelcomLightWrapper.LightColors.Green;
                    break;

                case LightColor.Yellow:
                    // Map yellow to the appropriate color based on what the light supports
                    result = lightSupportsYellow ? DelcomLightWrapper.LightColors.Yellow : DelcomLightWrapper.LightColors.Blue;
                    break;
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
            ButtonState currentState = ButtonState.NotPressed;
            int countButtonReleaseEvent = 0;
            int countButtonDownEvent = 0;
            Boolean buttonHeld = false;

            //The time when the button was last in a down state.
            DateTime lastButtonDownTime = DateTime.MinValue;

            //The time when the button was last in an up state.
            DateTime lastButtonUpTime = DateTime.MinValue;

            // Loop endlessly until we're asked to stop
            while (!this.stopRequested.WaitOne(0))
            {
                //Check if light is still connected
                bool isStillConnected = DelcomLightWrapper.isButtonConnected(this.deviceHandle);

                //If not still connected, start loop to poll for connection until it is connected.
                if (!isStillConnected)
                {
                    Trace.TraceInformation("BackgroundPollingWorker: closing device.");
                    DelcomLightWrapper.CloseDelcomDevice(this.deviceHandle);
                    Trace.TraceInformation("BackgroundPollingWorker: closed device. reopeing device.");
                    this.deviceHandle = DelcomLightWrapper.TryOpeningDelcomDevice();
                    Trace.TraceInformation("BackgroundPollingWorker: opened device.");
                }

                // We do not examine the button state at all for small duraiton (300ms default) after the button is up
                // last time in order to avoid two sequential 'pressed' events are fired unintentionally.
                if ((DateTime.UtcNow - lastButtonUpTime) > Properties.Settings.Default.DelcomButtonIgnoreAfterButtonUp)
                {
                    // Get the current state of the button
                    ButtonState newState = DelcomLightWrapper.DelcomGetButtonStatus(deviceHandle);

                    if (newState == ButtonState.Unknown)
                    {
                        throw new NotImplementedException();
                    }
                    else if (newState != currentState)
                    {
                        // The state has changed, so we need to handle it

                        if (newState == ButtonState.NotPressed)
                        {
                            // We were previously in a pressed state, but the button on the light is flaky, so we need
                            // to make sure the button has really been released, so we'll wait a few iterations.
                            countButtonReleaseEvent++;

                            if (countButtonReleaseEvent >= DelcomLight.ButtonRecognitionThreshold)
                            {
                                // Only remember the currentstate as changed if we're exceeding threshold
                                currentState = newState;

                                // Button just released so reset timer
                                countButtonReleaseEvent = 0;

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
                        else if (newState == ButtonState.Pressed)
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
                    else if (newState == ButtonState.Pressed) // && oldState == ButtonState.Pressed
                    {
                        // Button has been held, check if hold is greater than threshold
                        TimeSpan holdDuration = DateTime.UtcNow - lastButtonDownTime;

                        // Reset iterations since last release
                        countButtonReleaseEvent = 0;

                        // If button held event has already been fired, we don't want to fire again until the button has been released
                        if (!buttonHeld)
                        {
                            // If hold duration is greater than threshold we should fire a button held event
                            if (holdDuration > Properties.Settings.Default.DelcomButtonHoldThreshold)
                            {
                                this.stateMachine.PostInput(Input.ButtonHeld);
                                buttonHeld = true;
                            }
                        }
                    }
                    else if (newState == ButtonState.NotPressed) // && oldState == ButtonState.NotPressed
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