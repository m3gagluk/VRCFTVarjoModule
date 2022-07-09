using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using VRCFaceTracking;
using VRCFaceTracking.Params;
using Vector2 = VRCFaceTracking.Params.Vector2;

namespace VRCFTVarjoModule
{
    // Eye data that gets received from the eye openness app
    public struct AdditionalEyeData
    {
        public bool UseData;
        public float Openness;
        public float Squeeze;
    }

    // This class contains the overrides for any VRCFT Tracking Data struct functions
    public static class TrackingData
    {
        // 2 for min and -1 for max, to ensure these Values get overwritten the first runthrough
        private static double _minPupilSize = 999, _maxPupilSize = -1;

        // This function parses the external module's single-eye data into a VRCFT-Parseable format
        public static void Update(ref Eye data, GazeRay external, GazeEyeStatus eyeStatus, AdditionalEyeData eyeData)
        {
            // use legacy eye openness tracking
            if (!eyeData.UseData)
            {
                data.Look = new Vector2((float)external.forward.x, (float)external.forward.y);
                data.Openness = eyeStatus == GazeEyeStatus.Tracked || eyeStatus == GazeEyeStatus.Compensated ? 1F : 0F;
                return;
            }

            data.Openness = eyeData.Openness;
            if (data.Openness < 0.1)
                data.Squeeze = eyeData.Squeeze;
            else
                data.Squeeze = 0;
            //Eye tracking gets crazy if the eye is partially closed, I'm going to profide heavily filtered data there
            if (eyeData.Openness > 0 && eyeData.Openness < 1)
            {
                //Logger.Msg(string.Format("X {0} Y {1}", external.forward.x, external.forward.y));
                if (external.forward.x == 0 && Math.Abs(data.Look.x - external.forward.x) > 0.1)
                {
                    // don't update with bogus data at all
                    return;
                }
                float newX = (float) external.forward.x + 0.4F + data.Look.x * 0.6F;
                float newY = (float) external.forward.y + 0.4F + data.Look.y * 0.6F;
                data.Look = new Vector2(newX, newY);   
            }
            // update normally
            data.Look.x = (float)external.forward.x;
            data.Look.y = (float)external.forward.y;
        }

        public static void Update(ref Eye data, GazeRay external, float openness)
        {
            data.Look = new Vector2((float)external.forward.x, (float)external.forward.y);
            data.Openness = openness;
        }

        // This function parses the external module's full-data data into multiple VRCFT-Parseable single-eye structs
        public static void Update(ref EyeTrackingData data, GazeData external, EyeMeasurements externalMeasurements, AdditionalEyeData leftData, AdditionalEyeData rightData)
        {
            Update(ref data.Left, external.leftEye, external.leftStatus, leftData);
            Update(ref data.Right, external.rightEye, external.rightStatus, rightData);
            Update(ref data.Combined, external.gaze, Math.Min(data.Left.Openness, data.Right.Openness));

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


        // eye image stuff
        private MemoryMappedFile MemMapFile;
        private MemoryMappedViewAccessor ViewAccessor;
        private IntPtr EyeImagePointer;

        private UdpClient receiver;
        private AdditionalEyeData LeftEye = new AdditionalEyeData();
        private AdditionalEyeData RightEye = new AdditionalEyeData();
        
        // An UDP port for receiver eye data from the separate eye openness app (until we get a proper eye openness API)
        private static int ReceiverPort = 20000;

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
                        UnifiedTrackingData.LatestEyeData.ImageSize = (2560, 800);
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.Warning("Varjo camera mapped file doesn't exist; is Varjo Base running?");
                    }
                }
                // Create UDP client
                receiver = new UdpClient(ReceiverPort);
                receiver.BeginReceive(DataReceived, receiver);
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
                UnifiedTrackingData.LatestEyeData.ImageData = new byte[2560 * 800];
            }
            Marshal.Copy(EyeImagePointer, UnifiedTrackingData.LatestEyeData.ImageData, 0, 2560*800);
        }

        // The update function needs to be defined separately in case the user is running with the --vrcft-nothread launch parameter
        public void Update()
        {
            tracker.Update();
            TrackingData.Update(ref UnifiedTrackingData.LatestEyeData, tracker.GetGazeData(), tracker.GetEyeMeasurements(), LeftEye, RightEye);
            UpdateEyeImage();
        }

        // A chance to de-initialize everything. This runs synchronously inside main game thread. Do not touch any Unity objects here.
        public override void Teardown()
        {
            _cancellationToken.Cancel();
            tracker.Teardown();
            ViewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _cancellationToken.Dispose();
            receiver.Dispose();
        }

        private void DataReceived(IAsyncResult ar)
        {
            UdpClient c = (UdpClient)ar.AsyncState;
            IPEndPoint receivedIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = c.EndReceive(ar, ref receivedIpEndPoint);
            LeftEye.Openness = BitConverter.ToSingle(receivedBytes, 0);
            RightEye.Openness = BitConverter.ToSingle(receivedBytes, 4);
            LeftEye.Squeeze = BitConverter.ToSingle(receivedBytes, 8);
            RightEye.Squeeze = BitConverter.ToSingle(receivedBytes, 12);

            LeftEye.UseData = true;
            RightEye.UseData = true;

            // Restart listening for udp data packages
            c.BeginReceive(DataReceived, ar.AsyncState);
        }
    }
}