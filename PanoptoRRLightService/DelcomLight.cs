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
    public enum DelcomColor
    {
        Off,
        Red,
        Green,
        Yellow,
    }

    public class DelcomLight
    {
        public uint hUSB;
        private MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback;
        private int changeColorRequestId = 0;
        private bool shouldStop = false;
        private TimeSpan holdThreshold;
        TimeSpan minTimeBetweenClicks = TimeSpan.FromMilliseconds(RRLightProgram.Properties.Settings.Default.MintimeBetweenClicksMilliseconds);
        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="stateMachineInputCallback">delegate to call when there's an event to report</param>
        public DelcomLight(MainAppLogic.EnqueueStateMachineInput stateMachineInputCallback, TimeSpan holdTime)
        {
            hUSB = 0;

            // remember the delegate so we can invoke when we get input
            this.stateMachineInputCallback = stateMachineInputCallback;

            //Initialize hold threshold from argument passed from Main
            this.holdThreshold = holdTime;

            // start a background thread to poll the device for input
            BackgroundWorker bgw1 = new BackgroundWorker();
            bgw1.DoWork += delegate { this.BackgroundPollingWorker(); };
            bgw1.RunWorkerAsync();
        }


        /// <summary>
        ///     Change the color of the light
        /// </summary>
        /// <param name="inputColor"></param>
        public void ChangeColor(DelcomColor inputColor)
        {
            ChangeColor(inputColor, steady: true, duration: null);
        }

        /// <summary>
        ///     Change the color of the light, including options to flash or show for only a particular duration
        /// </summary>
        /// <param name="inputColor"></param>
        /// <param name="steady"></param>
        /// <param name="duration"></param>
        public void ChangeColor(DelcomColor inputColor, bool steady, TimeSpan? duration)
        {
            lock (this)
            {
                changeColorRequestId++;

                if (inputColor == DelcomColor.Off)
                {
                    if (!DelcomLightWrapper.DelcomLEDAllAction(this.hUSB, DelcomLightWrapper.LightStates.Off) && Program.RunFromConsole)
                    {
                        Trace.TraceInformation(DateTime.Now + ": LED failure: all off");
                        Trace.Flush();
                    }
                }
                else
                {
                    // convert from the publicly exposed color to the internal value
                    DelcomLightWrapper.LightColors color = ConvertColor(inputColor);

                    DelcomLightWrapper.LightStates action = steady ? DelcomLightWrapper.LightStates.On
                                                                   : DelcomLightWrapper.LightStates.Flash;

                    if (!DelcomLightWrapper.DelcomLEDAllAction(this.hUSB, DelcomLightWrapper.LightStates.Off) && Program.RunFromConsole)
                    {
                        Trace.TraceInformation(DateTime.Now + ": LED failure: all off");
                        Trace.Flush();
                    }
                    if (!DelcomLightWrapper.DelcomLEDAction(this.hUSB, color, action) && Program.RunFromConsole)
                    {
                        Trace.TraceInformation(DateTime.Now + ": LED failure: " + Enum.GetName(typeof(DelcomLightWrapper.LightColors), color) + 
                                                " " + Enum.GetName(typeof(DelcomLightWrapper.LightStates), action));
                        Trace.Flush();
                    }

                    // We need to only have the light on for the requested duration
                    if (duration != null)
                    {
                        // Create a callback that will safely turn the light off
                        TimerCallback callback = new TimerCallback(delegate(object state)
                        {
                            // Get the current button action (as of the callback)
                            int currentButtonAction = -1;
                            lock (this)
                            {
                                currentButtonAction = this.changeColorRequestId;
                            }

                            int rememberedButtonAction = (int)state;

                            // Compare the current buttonaction to the remembered action
                            if (currentButtonAction == rememberedButtonAction)
                            {
                                // Only turn the light off if they still match, otherwise we've moved on to a new action
                                if (!DelcomLightWrapper.DelcomLEDAllAction(this.hUSB, DelcomLightWrapper.LightStates.Off) && Program.RunFromConsole)
                                {
                                    Trace.TraceInformation(DateTime.Now + ": LED failure: all off");
                                    Trace.Flush();
                                }
                            }
                            else
                            {
                                // Another request has come in while waiting for the timer to fire, ignore the delayed request to turn the light off
                            }
                        });

                        // Make a timer
                        Timer ledTimer = new Timer(
                            callback,
                            changeColorRequestId,
                            duration.Value,
                            TimeSpan.Zero);
                    }
                }
            }
        }

        // Stop the background thread
        public void Stop()
        {
            this.shouldStop = true;
            DelcomLightWrapper.CloseDelcomDevice(hUSB);
        }

        /// <summary>
        ///  Runs on a background thread to monitor the button state and will dispatch events back to the main thread
        /// </summary>
        private void BackgroundPollingWorker()
        {
            ButtonState currentState = ButtonState.NotPressed;
            int iterationsSinceLastButtonRelease = 0;
            int iterationsSinceLastButtonDown = 0;
            const int buttonReleaseTolerance = 2; // magic number that works well in practice
            //Timer to determine how long the button has been held down for
            TimeSpan holdDuration = TimeSpan.Zero;
            //The time when the button was last in a down state. Used for hardware error correction.
            DateTime lastButtonDownTime = DateTime.MinValue;
            //The time when the button was last in an up state. Used for hardware error correction.
            DateTime lastButtonUpTime = DateTime.MinValue;
            //Boolean switch for determining if a button was held for longer than the hold threshold while it is still being held down.
            Boolean buttonHeld = false;

            // Loop endlessly until we're asked to stop
            while (!this.shouldStop)
            {
                //Check if light is still connected
                bool isStillConnected = DelcomLightWrapper.isButtonConnected(hUSB);

                //If not still connected, start loop to poll for connection until it is connected.
                if (!isStillConnected)
                {
                    Trace.TraceInformation(DateTime.Now + ": Light Button not connected");
                    Trace.Flush();

                    DelcomLightWrapper.CloseDelcomDevice(hUSB);
                    hUSB = DelcomLightWrapper.TryOpeningDelcomDevice();

                    Trace.TraceInformation(DateTime.Now + ": Light Button connected");
                    Trace.Flush();
                    StateMachine.StateMachineInputArgs buttonArgs = new StateMachine.StateMachineInputArgs(StateMachine.StateMachineInput.NoInput);
                    stateMachineInputCallback(buttonArgs);
                }

                if ((DateTime.UtcNow - lastButtonUpTime) > minTimeBetweenClicks)
                {
                    // Get the current state of the button
                    ButtonState newState = DelcomLightWrapper.DelcomGetButtonStatus(hUSB);

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
                            iterationsSinceLastButtonRelease++;

                            if (iterationsSinceLastButtonRelease > buttonReleaseTolerance)
                            {
                                // Only remember the currentstate as changed if we're outside of our tolerance
                                currentState = newState;

                                //Button just released so reset timer
                                iterationsSinceLastButtonRelease = 0;

                                //Fire a button up event. This will only cause the state machine to act if in a previewing state with nothing queued.
                                //It will turn off the red light that is on when the button is being held down in that case
                                if (stateMachineInputCallback != null)
                                {
                                    StateMachine.StateMachineInputArgs buttonUpArgs = new StateMachine.StateMachineInputArgs(StateMachine.StateMachineInput.ButtonUp);
                                    stateMachineInputCallback(buttonUpArgs);
                                }

                                //If a button held event was already fired, we don't want to fire a button pressed event in this case
                                if (!buttonHeld)
                                {
                                    // Notify that we've had a button press
                                    if (stateMachineInputCallback != null)
                                    {
                                        StateMachine.StateMachineInputArgs buttonArgs = new StateMachine.StateMachineInputArgs(StateMachine.StateMachineInput.ButtonPressed);
                                        stateMachineInputCallback(buttonArgs);
                                    }
                                }

                                lastButtonUpTime = DateTime.UtcNow;

                                //reset button held, as it is no longer being held if it was before
                                buttonHeld = false;
                            }
                        }
                        else if (newState == ButtonState.Pressed)
                        {
                            // We were previously in a pressed state, but the button on the light is flaky, so we need
                            // to make sure the button has really been released, so we'll wait a few iterations.
                            iterationsSinceLastButtonDown++;

                            if (iterationsSinceLastButtonDown > buttonReleaseTolerance)
                            {
                                // The button has just been pressed

                                // Only remember the current state as changed if we're outside of our tolerance
                                currentState = newState;

                                //Button just pressed so reset timer
                                iterationsSinceLastButtonRelease = 0;

                                //Fire a button down event. This will only cause the state machine to act if in a previewing state with nothing queued.
                                //It will turn on the red light that is on when the button is being held down in that case
                                if (stateMachineInputCallback != null)
                                {
                                    StateMachine.StateMachineInputArgs buttonArgs = new StateMachine.StateMachineInputArgs(StateMachine.StateMachineInput.ButtonDown);
                                    stateMachineInputCallback(buttonArgs);
                                }

                                //Set last button down time to current time, as button was just pressed down
                                lastButtonDownTime = DateTime.UtcNow;
                            }


                        }
                    }
                    else if (newState == ButtonState.Pressed)
                    {

                        //Button has been held, check if hold is greater than threshold
                        holdDuration = DateTime.UtcNow - lastButtonDownTime;

                        //Reset iterations since last release
                        iterationsSinceLastButtonRelease = 0;

                        //If button held event has already been fired, we don't want to fire again until the button has been released
                        if (!buttonHeld)
                        {
                            //If hold duration is greater than threshold we should fire a button held event
                            if (holdDuration > holdThreshold)
                            {
                                // Notify that we've had a button held
                                if (stateMachineInputCallback != null)
                                {
                                    StateMachine.StateMachineInputArgs buttonArgs = new StateMachine.StateMachineInputArgs(StateMachine.StateMachineInput.ButtonHeld, holdDuration);
                                    stateMachineInputCallback(buttonArgs);
                                }

                                //We fired a button held event, so set this to true to prevent the event from being fired again while the button is still being held down.
                                buttonHeld = true;
                            }
                        }
                    }
                    else if (newState == ButtonState.NotPressed)
                    {
                        //Reset iterations since last press
                        iterationsSinceLastButtonDown = 0;
                    }
                }

                System.Threading.Thread.Sleep(RRLightProgram.Properties.Settings.Default.LightPollingIntervalMS);
            }
        }

        /// <summary>
        ///     Helper to convert our 'public-facing' color enum to the delcom light.
        ///     Given that DelcomLightWrapper.LightColors.Yellow == DelcomLightWrapper.LightColors.Blue
        ///     (the byte we send to the light indicating which LED we want to change is the same whether
        ///     it supports yellow or blue), this makes no difference at present.
        /// </summary>
        /// <param name="inputColor"></param>
        /// <returns></returns>
        private DelcomLightWrapper.LightColors ConvertColor(DelcomColor inputColor)
        {
            DelcomLightWrapper.LightColors result = DelcomLightWrapper.LightColors.Red;

            // Make this a configurable value since delcom lights come in both RGY (the default) and RGB, and
            // we want to be able to work with both.
            bool lightSupportsYellow = RRLightProgram.Properties.Settings.Default.LightSupportsYellow;

            switch (inputColor)
            {
                case DelcomColor.Red:
                    result = DelcomLightWrapper.LightColors.Red;
                    break;

                case DelcomColor.Green:
                    result = DelcomLightWrapper.LightColors.Green;
                    break;

                case DelcomColor.Yellow:
                    // Map yellow to the appropriate color based on what the light supports
                    result = lightSupportsYellow ? DelcomLightWrapper.LightColors.Yellow : DelcomLightWrapper.LightColors.Blue;
                    break;
            }

            return result;
        }

    }
}