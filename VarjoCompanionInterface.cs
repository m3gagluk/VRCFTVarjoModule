using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using VRCFaceTracking;

namespace VRCFTVarjoModule
{

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector
    {

        public double x;
        public double y;
        public double z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GazeRay
    {
        public Vector origin;   //!< Origin of the ray.
        public Vector forward;  //!< Direction of the ray.
    }

    public enum GazeStatus : long
    {
        Invalid = 0,
        Adjust = 1,
        Valid = 2
    }

    public enum GazeEyeStatus : long
    {
        Invalid = 0,
        Visible = 1,
        Compensated = 2,
        Tracked = 3
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct GazeData
    {
        public GazeRay leftEye;                 //!< Left eye gaze ray.
        public GazeRay rightEye;                //!< Right eye gaze ray.
        public GazeRay gaze;                    //!< Normalized gaze direction ray.
        public double focusDistance;            //!< Estimated gaze direction focus point distance.
        public double stability;                //!< Focus point stability.
        public long captureTime;                //!< Varjo time when this data was captured, see varjo_GetCurrentTime()
        public GazeEyeStatus leftStatus;        //!< Status of left eye data.
        public GazeEyeStatus rightStatus;       //!< Status of right eye data.
        public GazeStatus status;               //!< Tracking main status.
        public long frameNumber;                //!< Frame number, increases monotonically.
        public double leftPupilSize;            //!< Normalized [0..1] left eye pupil size.
        public double rightPupilSize;           //!< Normalized [0..1] right eye pupil size.
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct GazeCalibrationParameter
    {
        [MarshalAs(UnmanagedType.LPStr)] public string key;
        [MarshalAs(UnmanagedType.LPStr)] public string value;
    }

    public enum GazeCalibrationMode
    {
        Legacy,
        Fast
    };

    public enum GazeOutputFilterType
    {
        None,
        Standard
    }

    public enum GazeOutputFrequency
    {
        MaximumSupported,
        Frequency100Hz,
        Frequency200Hz
    }

    public enum GazeEyeCalibrationQuality
    {
        Invalid = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GazeCalibrationQuality
    {
        public GazeEyeCalibrationQuality left;
        public GazeEyeCalibrationQuality right;
    }

    public class VarjoCompanionInterface
    {

        private MemoryMappedFile MemMapFile;
        private MemoryMappedViewAccessor ViewAccessor;
        public GazeData memoryGazeData;
        private Process CompanionProcess;

        public bool ConnectToPipe()
        {
            if (!VarjoAvailable())
            {
                Logger.Error("Varjo headset isn't detected");
                return false;
            }
            var modDir = GetModuleDir();


            CompanionProcess = new Process();
            CompanionProcess.StartInfo.WorkingDirectory = modDir;
            CompanionProcess.StartInfo.FileName = Path.Combine(modDir, "VarjoCompanion.exe");
            CompanionProcess.Start();

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    MemMapFile = MemoryMappedFile.OpenExisting("VarjoEyeTracking");
                    ViewAccessor = MemMapFile.CreateViewAccessor();
                    return true;
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine("VarjoEyeTracking mapped file doesn't exist; the companion app probably isn't running");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not open the mapped file: " + ex);
                    return false;
                }
                Thread.Sleep(500);
            }

            return false;
        }

        public void Update()
        {
            if (MemMapFile == null) return;
            ViewAccessor.Read(0, out memoryGazeData);
        }

        public void Teardown()
        {
            if (MemMapFile == null) return;
            //memoryGazeData.shutdown = true; // tell the companion app to shut down gracefully but it doesn't work anyway
            ViewAccessor.Write(0, ref memoryGazeData);
            MemMapFile.Dispose();
            CompanionProcess.Kill();
        }

        private string GetModuleDir()
        {
            return Utils.DataDirectory + "\\CustomLibs\\Varjo";
        }

        private bool VarjoAvailable()
        {
            // totally not now the official Varjo library works under the hood
            return File.Exists("\\\\.\\pipe\\Varjo\\InfoService");
        }
    }
}