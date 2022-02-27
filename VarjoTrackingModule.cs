using System;
using System.Threading;
using VRCFaceTracking;
using VRCFaceTracking.Params;
using Vector2 = VRCFaceTracking.Params.Vector2;

namespace VRCFTVarjoModule
{
   
    // This class contains the overrides for any VRCFT Tracking Data struct functions
    public static class TrackingData
    {
        // This function parses the external module's single-eye data into a VRCFT-Parseable format
        public static void Update(ref Eye data, GazeRay external, GazeEyeStatus eyeStatus)
        {
            data.Look = new Vector2((float) external.forward.x, (float) external.forward.y);
            data.Openness = eyeStatus == GazeEyeStatus.Tracked || eyeStatus == GazeEyeStatus.Compensated ? 1F : 0F;
            
        }

        public static void Update(ref Eye data, GazeRay external)
        {
            data.Look = new Vector2((float)external.forward.x, (float)external.forward.y);
        }

        // This function parses the external module's full-data data into multiple VRCFT-Parseable single-eye structs
        public static void Update(ref EyeTrackingData data, GazeData external)
        {
            Update(ref data.Right, external.rightEye, external.rightStatus);
            Update(ref data.Left, external.leftEye, external.leftStatus);
            Update(ref data.Combined, external.gaze);

        }

    }
    
    public class VarjoTrackingModule : ITrackingModule
    {
        private static readonly VarjoCompanionInterface tracker = new VarjoCompanionInterface();
        private static CancellationTokenSource _cancellationToken;

        // Synchronous module initialization. Take as much time as you need to initialize any external modules. This runs in the init-thread
        public (bool eyeSuccess, bool lipSuccess) Initialize(bool eye, bool lip)
        {
            Logger.Msg("Initializing Varjo module");
            bool pipeConnected = tracker.ConnectToPipe();
            return (pipeConnected, false);
        }

        // This will be run in the tracking thread. This is exposed so you can control when and if the tracking data is updated down to the lowest level.
        public Action GetUpdateThreadFunc()
        {
            _cancellationToken = new CancellationTokenSource();
            return () =>
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    Update();
                    Thread.Sleep(10);
                }
            };
        }

        // The update function needs to be defined separately in case the user is running with the --vrcft-nothread launch parameter
        public void Update()
        {
            tracker.Update();
            TrackingData.Update(ref UnifiedTrackingData.LatestEyeData, tracker.memoryGazeData);
        }

        // A chance to de-initialize everything. This runs synchronously inside main game thread. Do not touch any Unity objects here.
        public void Teardown()
        {
            _cancellationToken.Cancel();
            tracker.Teardown();
            _cancellationToken.Dispose();
        }


        public bool SupportsEye => true;
        public bool SupportsLip => false;
    }
}