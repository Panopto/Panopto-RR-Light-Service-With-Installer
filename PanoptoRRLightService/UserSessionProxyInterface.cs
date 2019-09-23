using System;
using System.ServiceModel;
using Panopto.RemoteRecorderAPI.V1;

namespace RRLightProgram
{
    public enum RecorderAction
    {
        Start,
        StartWebcast,
        Stop,
        Pause,
        Resume,
        StartNext,
        StopDelete,
        Extend,
        GetNext,
        Version,
        None
    }

    [ServiceContract]
    public interface IWindowsRecorderUserSessionTether
    {
        [OperationContract]
        void ReportState(RemoteRecorderState state);
        [OperationContract]
        RecorderActionInfo GetQueuedCommand();
        [OperationContract]
        bool IsTetherRunning();
        [OperationContract]
        void ReportActionResult(RecorderActionResult result);
    }
}
