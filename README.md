# Panopto-RR-Light-Service-With-Installer
Panopto Remote Recorder Light Button Service

## License
Copyright 2018 Panopto, Swivl, and University of Washington

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

## Code maintenance

All files except described below are written and maintained by Panopto.
* PanoptoRRLightService/Delcom/DelcomDLL.dll is provided and maintained by Delcom.
* PanoptoRRLightService/Kuando/BusylightSDK.dll is provided and maintained by Plenom a/s.
* Files in PanoptoRRLightService/SwivlChico are provided and maintained by Swivl.
    * Please contact Swivl support about Swivl device and the code specific to Swivl.
* Files in PanoptoRRLightService/Serial are provided by the courtesy of University of Washington. See README.console.md for more informaiton.
    * Panopto may not support this part of the code. If you see any issue, open an issure report on GitHub. The author may respond it, although there is no gurantee.

## Release
Installer is available from [release page]( https://github.com/Panopto/Panopto-RR-Light-Service-With-Installer/releases).

## Build environment
You may build the binary and installer from this source code with the following tools. If you need to create a new version, update the version number both in AssemblyInfo.cs and Product.wxs.

* Visual Studio 2015
* WiX Toolset 3.10
* .NET framework 4.5
