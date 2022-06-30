using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking;

namespace VRCFTVarjoModule
{
    //Varjo's structs used with both native library and companion
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
    public struct EyeMeasurements
    {
        public long frameNumber;                    //!< Frame number, increases monotonically.
        public long captureTime;                    //!< Varjo time when this data was captured, see varjo_GetCurrentTime()
        public float interPupillaryDistanceInMM;    //!< Estimated IPD in millimeters
        public float leftPupilIrisDiameterRatio;    //!< Ratio between left pupil and left iris.
        public float rightPupilIrisDiameterRatio;   //!< Ratio between right pupil and right iris.
        public float leftPupilDiameterInMM;         //!< Left pupil diameter in mm
        public float rightPupilDiameterInMM;        //!< Right pupil diameter in mm
        public float leftIrisDiameterInMM;          //!< Left iris diameter in mm
        public float rightIrisDiameterInMM;         //!< Right iris diameter in mm
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


    public abstract class VarjoInterface
    {
        protected GazeData gazeData;
        protected EyeMeasurements eyeMeasurements;

        public GazeData GetGazeData()
        {
            return gazeData;
        }

        public EyeMeasurements GetEyeMeasurements()
        {
            return eyeMeasurements;
        }

        public abstract void Teardown();
        public abstract bool Initialize();
        public abstract void Update();

        public abstract string GetName();

        protected string GetModuleDir()
        {
            return Utils.PersistentDataDirectory + "\\CustomLibs\\Varjo";
        }

        protected bool VarjoAvailable()
        {
            // totally not how the official Varjo library works under the hood
            return File.Exists("\\\\.\\pipe\\Varjo\\InfoService");
        }
        
    }


}
