// Swivl - Copyright 2017
// SwivlChico Dll CS.Net declaration class file
// Constants and Functions declaration

using System;
using System.Text;
using System.Runtime.InteropServices;

public class SwivlChico
{
    // Functions

    // Gets the Swivl Chico connection state
    [DllImport("chicntrl.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Chico_IsConnected();

    // Gets the Swivl Chico Record button state
    [DllImport("chicntrl.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Chico_GetRecordButtonState();

    // Sets the LED color
    [DllImport("chicntrl.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Chico_SetColor(byte color);

}       // end of class