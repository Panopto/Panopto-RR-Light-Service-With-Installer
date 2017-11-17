# Panopto Remote Recorder Light and Serial Service

## Serial Communication

### Configuration
Select Serial as the installer option. Set the appropriate values in RRLightService.exe.config in the install directory. e.g.:

````
            <setting name="SerialPortName" serializeAs="String">
                <value>COM4</value>
            </setting>
            <setting name="SerialPortBaudRate" serializeAs="String">
                <value>9600</value>
            </setting>
            <setting name="SerialPortParity" serializeAs="String">
                <value>None</value>
            </setting>
            <setting name="SerialPortDataBits" serializeAs="String">
                <value>8</value>
            </setting>
            <setting name="SerialPortStopBits" serializeAs="String">
                <value>One</value>
            </setting>
````

### Input

Input is limited to the following simple commands:

Command | Description
--------|------------------------------------------
START   | Opt-in to a "Potential Recording", immediately start a queued recording, or start recording a new session.
STOP    | Stop the current recording, or opt-out (and delete) if this is a "Potential Recording"
PAUSE   | Pause the current recording
RESUME  | Resume the current (paused) recording
EXTEND  | Extend the current recording by 5 minutes
STATUS  | Get info about the current state of the recorder, and the current or next recording, if any

### Output

#### Serial Error response

Issuing an unknown command will result in the error message:
````
Serial-Error: Command not found: <command>
````

#### Event Responses

Responses are output as events occur. The response consists of:
````
<Event Name> OK
````
if the event was processed successfully, or
````
<Event Name> Error
````
if the event was not able to process, or
````
<Event Name> Ignored
````
If the event would have no effect (such as trying to STOP when there is no current recording).

Possible events are:

Event Name                         | Description
-----------------------------------|---------------------------
None                               | An empty event
RecorderPreviewingNoNextSchedule   | Remote Recorder is previewing
RecorderPreviewingWithNextSchedule | Remote Recorder is previewing, and a recording is queued
RecorderRecording                  | Remote Recorder is recording
RecorderStartedPotentialRecording  | Remote Recorder has started a "Potential Recording"
RecorderPaused                     | Remote Recorder is paused
RecorderStopped                    | Remote Recorder is stopped
RecorderDormant                    | Remote Recorder is blocked, i.e. the local recorder is Running
RecorderFaulted                    | Remote Recorder is faulted
RecorderDisconnected               | Remote Recorder is disconnected, or not found
ButtonPressed                      | Button pressed for less time than the hold threshold
ButtonHeld                         | Button held down for longer than the hold threshold
ButtonDown                         | Button is pressed down
ButtonUp                           | Button is released
OptInTimerElapsed                  | The opt-in timer for a "Potential Recording" has elapsed
CommandStart                       | A START command was issued
CommandStop                        | A STOP command was issued
CommandPause                       | A PAUSE command was issued
CommandResume                      | A RESUME command was issued
CommandExtend                      | An EXTEND command was issued

#### Status

In response to a STATUS command, data will be output in the format:
```
<keyword>: <value>
```

Possble data keywords are:

Keyword                                | Description
---------------------------------------|---------------------------------------------
Recorder-Status                        | The current state of the remote recorder
CurrentRecording-Id                    | Id (GUID) of the current recording
CurrentRecording-Name                  | Name of the current recording
CurrentRecording-StartTime             | Start time of the current recording
CurrentRecording-EndTime               | End time of the current recording
CurrentRecording-MinutesUntilStartTime | Minutes until start of the current recording
CurrentRecording-MinutesUntilEndTime   | Minutes until end of the current recording
NextRecording-Id                       | Id (GUID) of the queued recording
NextRecording-Name                     | Name of the queued recording
NextRecording-StartTime                | Start time of the queued recording
NextRecording-EndTime                  | End time of the queued recording
NextRecording-MinutesUntilStartTime    | Minutes until start of the queued recording
NextRecording-MinutesUntilEndTime      | Minutes until end of the queued recording

Possible values for Recorder-Status are:

Recorder-Status            | Description
---------------------------|----------------------------------------------------------------
Init                       | Service is initializing, Remote Recorder state unknown
PreviewingNoNextSchedule   | Previewing (Idle)
PreviewingWithNextSchedule | Previewing, a recording is queued to start within the next hour
TransitionAnyToRecording   | Previewing, attempting to Start Recording
PotentialRecording         | Recording a "Potential Recording"
Recording                  | Recording
TransitionRecordingToPause | Recording, attempting to Pause
TransitionPausedToRecording| Paused, attempting to Resume Recording
Paused                     | Paused
TransitionPausedToStop     | Paused, attempting to Stop
TransitionRecordingToStop  | Recording, attempting to Stop
Stopped                    | Stopped
Dormant                    | Blocked, i.e. the Local Recorder is Running
Faulted                    | Faulted
Disconnected               | Disconnected, or not found
