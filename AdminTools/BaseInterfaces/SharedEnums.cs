namespace MiracleAdmin
{
    namespace Shared
    {
        public enum ServiceCommands : byte
        {
            StartEngine = 0x32, StopEngine, UpdateSVNEngine, StartPC, RestartPC, ShutdownPC
        };

        public enum GrabberCommand : byte
        {
            StartGrabber = 0x64, StopGrabber
        };

        public enum EngineCommands : byte
        {
            TimeSync = 0x30, Command
        };

        public enum UploadThumbnailCommands : byte
        {
            Begin, Sync, End
        };

        public enum UploadThumbnailStates : byte
        {
            Unsync, Begin, SyncProgress, End, Sync
        };

        public enum CreateResourceCommands : byte
        {
            Reset, Create
        };
    }
}