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
        // 2 for min and -1 for max, to ensure these Values get overwritten the first runthrough
        private static double _minPupilSize = 999, _maxPupilSize = -1;

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
        public static void Update(ref EyeTrackingData data, GazeData external, EyeMeasurements externalMeasurements)
        {
            Update(ref data.Right, external.rightEye, external.rightStatus);
            Update(ref data.Left, external.leftEye, external.leftStatus);
            Update(ref data.Combined, external.gaze);

            // Determines whether the pupil Size/Eye dilation
            // If one is open and the other closed, we don't want the closed one to pull down the Values of the open one.
            double pupilSize = 0;
            // Casting the status as ints allows for easier comparison; as we need Compensated (2) or Tracked (3), that means >= 2
            if ((int)external.leftStatus >= 2 && (int)external.rightStatus >= 2)
            {
                pupilSize = (externalMeasurements.leftPupilDiameterInMM + externalMeasurements.rightPupilDiameterInMM) / 2;
            }
            else if ((int)external.rightStatus >= 2)
            {
                pupilSize = externalMeasurements.rightPupilDiameterInMM;
            }
            else if ((int)external.leftStatus >= 2)
            {
                pupilSize = externalMeasurements.leftPupilDiameterInMM;
            }

            // Only set the Eye Dilation, if we actually have Pupil data
            if (pupilSize > 0)
            {
                data.EyesDilation = (float)calculateEyeDilation(ref pupilSize);
            }
        }

        // This Function is used to calculate the Eye Dilation based on the lowest and highest measured Pupil Size
        private static double calculateEyeDilation(ref double pupilSize)
        {
            // Adjust the bounds if Pupil Size exceeds the last thought maximum bounds
            if (pupilSize > _maxPupilSize)
            {
                _maxPupilSize = pupilSize;
            }
            if (pupilSize < _minPupilSize)
            {
                _minPupilSize = pupilSize;
            }

            // In case both max and min are the same, we need to return 0.5; Don't wanna run into a divide by 0 situation ^^"
            // We also don't want to run the maths if the pupil size bounds haven't been initialized yet...
            if (_maxPupilSize == _minPupilSize || _maxPupilSize == -1)
            {
                return 0.5;
            }

            // Pretty typical number range convertion.
            // We assume we want 1 for max dilation and 0 for min dilation; simplifies the maths a bit
            return (pupilSize - _minPupilSize) / (_maxPupilSize - _minPupilSize);
        }

    }
    
    public class VarjoTrackingModule : ExtTrackingModule 
    {
        private static VarjoInterface tracker;
        private static CancellationTokenSource _cancellationToken;
        public override (bool SupportsEye, bool SupportsLip) Supported => (true, false);
        public override (bool UtilizingEye, bool UtilizingLip) Utilizing { get; set; }

        // Synchronous module initialization. Take as much time as you need to initialize any external modules. This runs in the init-thread
        public override (bool eyeSuccess, bool lipSuccess) Initialize(bool eye, bool lip)
        {
            if (IsStandalone())
            {
                tracker = new VarjoNativeInterface();
            }
            else
            {
                tracker = new VarjoCompanionInterface();
            }
            Logger.Msg(string.Format("Initializing {0} Varjo module", tracker.GetName()));
            bool pipeConnected = tracker.Initialize();
            return (pipeConnected, false);
        }

        // Detects if the module is running in the standalone version of VRCFT
        private bool IsStandalone()
        {
            return true; // uuuh that will do anyway
        }

        // This will be run in the tracking thread. This is exposed so you can control when and if the tracking data is updated down to the lowest level.
        public override Action GetUpdateThreadFunc()
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
        public override void Update()
        {
            tracker.Update();
            TrackingData.Update(ref UnifiedTrackingData.LatestEyeData, tracker.GetGazeData(), tracker.GetEyeMeasurements());
        }

        // A chance to de-initialize everything. This runs synchronously inside main game thread. Do not touch any Unity objects here.
        public override void Teardown()
        {
            _cancellationToken.Cancel();
            tracker.Teardown();
            _cancellationToken.Dispose();
        }
    }
}