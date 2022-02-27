using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking;

namespace VRCFTVarjoModule
{
    class VarjoNativeInterface : VarjoInterface
    {
        private IntPtr _session;

        public override bool Initialize()
        {
            if (!VarjoAvailable())
            {
                Logger.Error("Varjo headset isn't detected");
                return false;
            }
            LoadLibrary();
            _session = varjo_SessionInit();
            if (_session == IntPtr.Zero)
            {
                return false;
            }
            if (!varjo_IsGazeAllowed(_session))
            {
                Logger.Error("Gaze tracking is not allowed! Please enable it in the Varjo Base!");
                return false;
            }
            varjo_GazeInit(_session);
            varjo_SyncProperties(_session);
            return true;
        }

        public override void Teardown()
        {
            throw new NotImplementedException();
        }

        public override void Update()
        {
            if (_session == IntPtr.Zero)
                return;

            gazeData = varjo_GetGaze(_session);
        }
        public override string GetName()
        {
            return "native DLL";
        }

        private bool LoadLibrary()
        {
            // absolutely stolen from the main binary
            string path = GetModuleDir() + "\\VarjoLib.dll";
            if (LoadLibrary(path) == IntPtr.Zero)
            {
                Logger.Error(string.Concat("Unable to load library ", path));
                return true;
            }
            Logger.Msg(string.Concat("Loaded library ", path));
            return true;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = false, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_IsAvailable();

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern IntPtr varjo_SessionInit();

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_SessionShutDown(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_GazeInit(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern int varjo_GetError(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern string varjo_GetErrorDesc(int errorCode);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_IsGazeAllowed(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_IsGazeCalibrated(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern GazeData varjo_GetGaze(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_RequestGazeCalibration(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_GetPropertyBool(IntPtr session, int propertyKey);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern int varjo_GetPropertyInt(IntPtr session, int propertyKey);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_SyncProperties(IntPtr session);

    }
}
