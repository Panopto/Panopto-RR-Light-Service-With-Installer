# Panopto-RR-Light-Service-With-Installer
Panopto Remote Recorder Light Button Service, including a TCP server for remote control.

This is a **BETA** version.

## TCP Server

This version incorporates a TCP server written by [Craig Baird on Codeproject] (http://www.codeproject.com/Articles/488668/Csharp-TCP-Server).

Its operation is based upon the implementation of the serial interface [implemented by the University of Washington] (https://github.com/uw-it-cte/Panopto-RR-Light-Service-With-Installer), with the serial interface being replaced by a TCP server. Though serial is inherantly more reliable than ethernet, the rational behind this method over the serial method allows the inclusion of remote interaction with the recorder easily in code, rather than requiring a physical serial interface. This is useful where providing a serial cable to the remote recorder may not be practical, i.e. distance/cable route to a control system or a limited availability of COM ports.

## Usage

The server will start automatically and open an interface on port 3000. This can be configured using the settings file.

```
            <setting name="TcpServer" serializeAs="String">
                <value>True</value>
            </setting>
            <setting name="TcpServerPort" serializeAs="String">
                <value>3000</value>
            </setting>
```

Command | Description 
--------|------------------------------------------
START   | If a recording is queued, start it now. Otherwise start recording a new session.
STOP    | Stop the current recording
PAUSE   | Pause the current recording
RESUME  | Resume the current (paused) recording

Commands should be terminated with `\n\r`

## Responses

Recorder-State             | Description
---------------------------|----------------------------------------------------------------
Init                       | Service is initializing, Remote Recorder state unknown
PreviewingNoNextSchedule   | Previewing (Idle)
PreviewingWithNextSchedule | Previewing, a recording is queued to start within the next hour
TransitionAnyToRecording   | Transitioning to a recording from previewing
Recording                  | Recording
TransitionRecordingToPause | Transitioning from Record to Pause
Paused                     | Paused
TransitionPausedToStop     | Transitioning to Stop from Pause
TransitionRecordingToStop  | Transitioning from Record to Stop
Stopped                    | Stopped
Dormant                    | Dormant, i.e. the Local Recorder is Running
Faulted                    | Faulted
Disconnected               | Disconnected, or not found

## Warning

This TCP server has no authentication mechanism and it is advised that a firewall rule be created to allow only permitted clients to connect to it.

## Notes
* The TCP server can handle multiple clients, though this has not been load tested.

## Todo
* Reimplement detail status as per UW implementation.

## License
Copyright (c) 2016 Panopto

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Release
Installer is available from [release page]( https://github.com/Panopto/Panopto-RR-Light-Service-With-Installer/releases).

## Build environment
You may build the binary and installer from this source code with the following tools. If you need to create a new version, update the version number both in AssemblyInfo.cs and Product.wxs.

* Visual Studio 2015
* WiX Toolset 3.10
* .NET framework 4.5
