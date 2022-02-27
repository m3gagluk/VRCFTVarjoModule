using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using VRCFaceTracking;

namespace VRCFTVarjoModule
{

    public class VarjoCompanionInterface : VarjoInterface
    {
        private MemoryMappedFile MemMapFile;
        private MemoryMappedViewAccessor ViewAccessor;
        private Process CompanionProcess;

        public override bool Initialize()
        {
            if (!VarjoAvailable())
            {
                Logger.Error("Varjo headset isn't detected");
                return false;
            }
            string modDir = GetModuleDir();
            string exePath = Path.Combine(modDir, "VarjoCompanion.exe");
            if (!File.Exists(exePath))
            {
                Logger.Error("VarjoCompanion executable wasn't found!");
                return false;
            }
            CompanionProcess = new Process();
            CompanionProcess.StartInfo.WorkingDirectory = modDir;
            CompanionProcess.StartInfo.FileName = exePath;
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

        public override void Update()
        {
            if (MemMapFile == null) return;
            ViewAccessor.Read(0, out gazeData);
        }

        public override void Teardown()
        {
            if (MemMapFile == null) return;
            //memoryGazeData.shutdown = true; // tell the companion app to shut down gracefully but it doesn't work anyway
            ViewAccessor.Write(0, ref gazeData);
            MemMapFile.Dispose();
            CompanionProcess.Kill();
        }

        public override string GetName()
        {
            return "companion";
        }
    }
}