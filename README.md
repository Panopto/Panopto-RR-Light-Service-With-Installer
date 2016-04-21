# Panopto Remote Recorder Light and Serial Service

Download the installable service here: https://github.com/uw-it-cte/Panopto-RR-Light-Service-With-Installer/releases/latest

## Light Button

See http://support.panopto.com/documentation/recording/remote-recorder/utilizing-remote-recorder-usb-visual-signal-indicator

## Serial Communication

### Configuration
Serial communication will not work without configuration. Copy RRLightService.exe.config to the install directory, and enter the appropriate values. e.g.:

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
START   | If a recording is queued, start it now. Otherwise start recording a new session.
STOP    | Stop the current recording
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

#### Action Responses

Responses are output as actions occur. The response consists of:
````
<Action Name> OK
````
if the action was processed successfully, or
````
<Action Name> ERROR
````
if the action was not able to process. Note that actions that have no effect (such as trying to PAUSE or STOP when there is no current recording) will still result in an OK.

Possible actions are:

Action Name              | Description
-------------------------|---------------------------
NoInput                  | An empty action
RecorderPreviewing       | Remote Recorder is Previewing
RecorderRecording        | Remote Recorder is Recording
RecorderPaused           | Remote Recorder is Paused
RecorderFaulted          | Remote Recorder is Faulted
RecorderPreviewingQueued | Remote Recorder is Previewing, and a recording is queued
RecorderStopped          | Remote Recorder is Stopped
RecorderRunning          | Remote Recorder is Blocked, i.e. the Local Recorder is Running
Disconnected             | Remote Recorder is Disconnected, or not found
ButtonPressed            | A connected Light Button was pressed for less time than the hold threshold
ButtonHeld               | A connected Light Button was held down for longer than the hold threshold
ButtonDown               | A connected Light Button is pressed down
ButtonUp                 | A connected Light Button is released
CommandStart             | A START command was issued
CommandStop              | A STOP command was issued
CommandPause             | A PAUSE command was issued
CommandResume            | A RESUME command was issued
CommandExtend            | An EXTEND command was issued

#### Status

In response to a STATUS command, data will be output in the format:
```
<keyword>: <value>
```

Possble data keywords are:

Keyword                         | Description
--------------------------------|---------------------------------------------
Recorder-State                  | The current state of the remote recorder
Recording-Id                    | Id (GUID) of the current recording
Recording-Name                  | Name of the current recording
Recording-StartTime             | Start time of the current recording
Recording-EndTime               | End time of the current recording
Recording-MinutesUntilStartTime | Minutes until start of the current recording
Recording-MinutesUntilEndTime   | Minutes until end of the current recording
Queued-Id                       | Id (GUID) of the queued recording
Queued-Name                     | Name of the queued recording
Queued-StartTime                | Start time of the queued recording
Queued-EndTime                  | End time of the queued recording
Queued-MinutesUntilStartTime    | Minutes until start of the queued recording
Queued-MinutesUntilEndTime      | Minutes until end of the queued recording

Possible values for Recorder-State are:

Recorder-State     | Description
-------------------|----------------------------------------------------------------
Init               | Service is initializing, Remote Recorder state unknown
RRPreviewing       | Previewing (Idle)
RRPreviewingQueued | Previewing, a recording is queued to start within the next hour
RRRecordingWait    | Paused or Previewing, attempting to Resume/Start
RRRecording        | Recording
RRPausedWait       | Recording, attempting to Pause
RRPaused           | Paused
RRStoppingPaused   | Paused, attempting to Stop
RRStoppingRecord   | Recording, attempting to Stop
RRStopped          | Stopped
RRRunning          | Blocked, i.e. the Local Recorder is Running
RRFaulted          | Faulted
RRDisconnected     | Disconnected, or not found
