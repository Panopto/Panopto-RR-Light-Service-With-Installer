using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RRLightProgram
{
    public class MainLogic
    {
        //Boolean used for stopping thread loop
        private bool shouldStop = false;

        //Initialize queue for events to input into statemachine
        private Queue<StateMachine.StateMachineInputArgs> stateMachineInputQueue = new Queue<StateMachine.StateMachineInputArgs>();

        //Initialize threshold for button hold from settings to pass into light.


        // Delegate for the light and the RR to callback to add statemachine input to the queue (in a threadsafe manner)
        public delegate void EnqueueStateMachineInput(StateMachine.StateMachineInputArgs input);

        /// <summary>
        ///     Program main loop
        /// </summary>
        public void ThreadMethod()
        {
            //Create new DelcomLight object and start it's thread to listen for input from the button
            DelcomLight dLight = new DelcomLight(new EnqueueStateMachineInput(this.AddInputToStateMachineQueue),
                                       RRLightProgram.Properties.Settings.Default.HoldDuration);

            //Create new remote recorder sync object to poll recorder state and input changes into state machine
            RemoteRecorderSync rSync = new RemoteRecorderSync(new EnqueueStateMachineInput(this.AddInputToStateMachineQueue));

            //Initialize state machine. Pass in Light and RemoteRecorder
            StateMachine sm = new StateMachine(dLight, rSync);

            // Main thread loop
            // Loop endlessly until we're asked to stop
            while (!this.shouldStop)
            {
                StateMachine.StateMachineInputArgs argsToProcess = null;

                // lock only while we're inspecting and changing the queue
                lock (stateMachineInputQueue)
                {
                    // if the queue has anything, then work on it
                    if (stateMachineInputQueue.Any())
                    {
                        // dequeue
                        argsToProcess = stateMachineInputQueue.Dequeue();
                    }
                }

                if (argsToProcess != null)
                {
                    // send the input to the state machine
                    sm.ProcessStateMachineInput(argsToProcess);
                }
                else
                {
                    // else sleep
                    Thread.Sleep(50);
                }
            }
        }

        private void AddInputToStateMachineQueue(StateMachine.StateMachineInputArgs input)
        {
            TraceVerbose.Trace("Detected input: {0}", input.Input);

            lock (stateMachineInputQueue)
            {
                stateMachineInputQueue.Enqueue(input);
            }
        }

        public void Stop()
        {
            shouldStop = true;
        }
    }
}
