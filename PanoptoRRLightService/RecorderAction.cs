using System;
using APIRecording = Panopto.RemoteRecorderAPI.V1.Recording;

namespace RRLightProgram
{
    public class RecorderActionResult
    {
        public Guid CommandId { get; set; }
        public bool BoolResult { get; set; }
        public Version VersionResult { get; set; }
        public APIRecording ApiResult { get; set; }
        public DateTime ResultTimestamp = DateTime.UtcNow;
    }

    public class RecorderActionInfo
    {
        public RecorderAction Action { get; set; }
        public Guid SessionId { get; set; }
        public Guid CommandId { get; set; }
    }
}
