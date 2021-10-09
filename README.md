# Panopto Recorder Light Button Service
This application enables showing recorder state on light devices (like Kuando Busylight or Delcom USB Visual Signal Indicator) and also enables basic start, stop and pause commands from these devices. The current version works with Panopto Remote recorder version 6 and above as well as Panopto for Windows version 7 and above.


## Product Release
Panopto provides the binary package of this application at [release page]( https://github.com/Panopto/Panopto-RR-Light-Service-With-Installer/releases).

## Support

### Binary Package
- Panopto provides direct support for the current version of the application for Delcom light devices and Kuando light devices based on the support contract. Please contact Panopto support through your organization's designated support contacts.
- Swivl provides direct support for Swivl devices.Panopto provides the direct support for the current version of the application binary package for Delcom light devices and Kuando light devices based on the support contract. Please contact Panopto support through your organization's designated support contacts p
- Serial device is not supported by anyone. Serial device code was originally made for Crestron device integration by a Panopto customer (not by Crestron), but Crestron now provides the integration module which is *not* based on this code.

### Code Level Support
Panopto does not provide direct support for modifying this code. The customers may fork this repository and make changes. Customers are welcome to exchange ideas and technical details in the community.

🛑 **Important Note**: The interface of **RemoteRecorderAPI.dll** is private and suject to change in the future version of the remote recorders. Panopto will update this service at that time, but that change may not keep the backward compatibility of DLL. Panopto recommends to avoid using this DLL outside of this code base.

## Code maintenance

All files except described below are written and maintained by Panopto.
* PanoptoRRLightService/Delcom/DelcomDLL.dll is provided and maintained by Delcom.
* PanoptoRRLightService/Kuando/BusylightSDK.dll is provided and maintained by Plenom a/s.
* Files in PanoptoRRLightService/SwivlChico are provided and maintained by Swivl.
    * Please contact Swivl support about Swivl device and the code specific to Swivl.
* Files in PanoptoRRLightService/Serial were provided by the courtesy of University of Washington. Original author does not support this code. The code is kept in this repository for as-is reference purpose.

## Build environment
You may build the binary and installer from this source code with the following tools. If you need to create a new version, update the version number both in AssemblyInfo.cs and Product.wxs.

* Visual Studio 2017
* WiX Toolset 3.11

## License
Copyright 2019 Panopto, Swivl, and University of Washington

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
