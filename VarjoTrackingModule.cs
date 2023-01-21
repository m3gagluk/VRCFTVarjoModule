using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using VRCFaceTracking;
using VRCFaceTracking.Params;
using Vector2 = VRCFaceTracking.Params.Vector2;

namespace VRCFTVarjoModule
{
   
    // This class contains the overrides for any VRCFT Tracking Data struct functions
    public static class TrackingData
    {
        enum VarjoOpennessMode : byte
        {
            Squeeze = 0,
            Openness = 1,
            Widen = 2
        }

        // Magic numbers to disect the 0-1 Varjo Openness float into SRanipal Openness, Widen & Squeeze values
        // Based on Testing from @Chickenbread; may need adjusting
        private static readonly float EYE_SQUEEZE_THRESHOLD = 0.15f, EYE_WIDEN_THRESHOLD = 0.90f;
        // Threshold of the maximum opening in Eye Openness that will be tracked as long as the eye status is "invalid"
        private static readonly float MAX_OPENNESS_DEVIATION = 0.1f;

        // 999 for min and -1 for max, to ensure these Values get overwritten the first runthrough
        private static double _minPupilSize = 999, _maxPupilSize = -1;

        // This function parses the external module's single-eye data into a VRCFT-Parseable format
        public static void Update(ref Eye data, GazeRay external, float openness, GazeEyeStatus eyeStatus)
        {
            if ((int)eyeStatus >= 2)
            {
                data.Look = new Vector2((float)external.forward.x, (float)external.forward.y);
            }

            parseOpenness(ref data, openness, eyeStatus >= GazeEyeStatus.Compensated);
        }

        public static void Update(ref Eye data, GazeRay external, float openness, GazeStatus combinedStatus)
        {
            if (combinedStatus == GazeStatus.Valid)
            {
                data.Look = new Vector2((float)external.forward.x, (float)external.forward.y);
            }

            parseOpenness(ref data, openness, combinedStatus != GazeStatus.Invalid);
        }

        // This function parses the external module's full-data data into multiple VRCFT-Parseable single-eye structs
        public static void Update(ref EyeTrackingData data, GazeData external, EyeMeasurements externalMeasurements)
        {
            Update(ref data.Right, external.rightEye, externalMeasurements.rightEyeOpenness, external.rightStatus);
            Update(ref data.Left, external.leftEye, externalMeasurements.leftEyeOpenness, external.leftStatus);
            Update(ref data.Combined, external.gaze, (externalMeasurements.leftEyeOpenness + externalMeasurements.rightEyeOpenness) / 2, external.status);

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
            if (pupilSize > 0 && external.status == GazeStatus.Valid)
            {
                data.EyesDilation = (float)calculateEyeDilation(ref pupilSize);
            }
            // Set the Pupil Diameter anyways
            data.EyesPupilDiameter = (float)(pupilSize > 10 ? 1 : pupilSize / 10);
        }

        // This function is used to calculate the Eye Dilation based on the lowest and highest measured Pupil Size
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

        // This function is used to disect the single Varjo Openness Float into the SRanipal Openness, Widen & Squeeze values
        // As the three SRanipal Parameters are exclusive to one another (if one is between 0 or 1, the others have to be either 0 or 1), we only need to do maths for one parameter
        private static void parseOpenness(ref Eye data, float openness, bool trackingValid)
        {
            float srOpenness;
            VarjoOpennessMode mode;


            if (openness <= EYE_SQUEEZE_THRESHOLD)
            {
                srOpenness = 0;
                mode = VarjoOpennessMode.Squeeze;
            }
            else if (openness >= EYE_WIDEN_THRESHOLD)
            {
                srOpenness = 1;
                mode = VarjoOpennessMode.Widen;
            }
            else
            {
                srOpenness = (openness - EYE_SQUEEZE_THRESHOLD) / (EYE_WIDEN_THRESHOLD - EYE_SQUEEZE_THRESHOLD);
                mode = VarjoOpennessMode.Openness;
            }

            if (trackingValid || srOpenness < data.Openness + MAX_OPENNESS_DEVIATION)
            {
                data.Openness = srOpenness;

                switch(mode)
                {
                    case VarjoOpennessMode.Squeeze:
                        data.Squeeze = (openness / -EYE_SQUEEZE_THRESHOLD) + 1;
                        data.Widen = 0;
                        break;
                    case VarjoOpennessMode.Widen:
                        data.Squeeze = 0;
                        data.Widen = (openness - EYE_WIDEN_THRESHOLD) / (1 - EYE_WIDEN_THRESHOLD);
                        break;
                    default:
                        data.Squeeze = 0;
                        data.Widen = 0;
                        break;
                }
            }
        }

    }
    
    public class VarjoTrackingModule : ExtTrackingModule 
    {
        private static VarjoInterface tracker;
        private static CancellationTokenSource _cancellationToken;


        // eye image stuff
        private MemoryMappedFile MemMapFile;
        private MemoryMappedViewAccessor ViewAccessor;
        private IntPtr EyeImagePointer;
        // Values for the Camera buffer size in VRCFT
#if GEN3HMD
        private static readonly int CAMERA_WIDTH = 1280, CAMERA_HEIGHT = 400; // 3rd Gen Varjo HMDs (VR-3, XR-3, Aero)
#else
        private static readonly int CAMERA_WIDTH = 2560, CAMERA_HEIGHT = 800; // 1st & 2nd Gen Varjo HMDs (VR-1, VR-2, XR-1)
#endif


        public override (bool SupportsEye, bool SupportsLip) Supported => (true, false);

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
            if (pipeConnected)
            {
                unsafe
                {
                    try
                    {
                        MemMapFile = MemoryMappedFile.OpenExisting("Global\\VarjoTrackerInfo");
                        ViewAccessor = MemMapFile.CreateViewAccessor();
                        byte* ptr = null;
                        ViewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                        EyeImagePointer = new IntPtr(ptr);
                        UnifiedTrackingData.LatestEyeData.SupportsImage = true;
                        UnifiedTrackingData.LatestEyeData.ImageSize = (CAMERA_WIDTH, CAMERA_HEIGHT);
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.Warning("Varjo camera mapped file doesn't exist; is Varjo Base running?");
                    }
                }
            }
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

        private unsafe void UpdateEyeImage()
        {
            if (MemMapFile == null || EyeImagePointer == null || !UnifiedTrackingData.LatestEyeData.SupportsImage)
            {
                return;
            }
            if (UnifiedTrackingData.LatestEyeData.ImageData == null)
            {
                UnifiedTrackingData.LatestEyeData.ImageData = new byte[CAMERA_WIDTH * CAMERA_HEIGHT];
            }
            Marshal.Copy(EyeImagePointer, UnifiedTrackingData.LatestEyeData.ImageData, 0, CAMERA_WIDTH * CAMERA_HEIGHT);
        }

        // The update function needs to be defined separately in case the user is running with the --vrcft-nothread launch parameter
        public void Update()
        {
            if (Status.EyeState == ModuleState.Active)
            {
                tracker.Update();
                TrackingData.Update(ref UnifiedTrackingData.LatestEyeData, tracker.GetGazeData(), tracker.GetEyeMeasurements());
            }
            UpdateEyeImage();
        }

        // A chance to de-initialize everything. This runs synchronously inside main game thread. Do not touch any Unity objects here.
        public override void Teardown()
        {
            _cancellationToken.Cancel();
            tracker.Teardown();
            ViewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _cancellationToken.Dispose();
        }
    }
}