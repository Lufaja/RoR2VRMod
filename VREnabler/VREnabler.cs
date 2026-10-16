using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VRPatcher
{
    public static class VRDependenciesPatcher
    {
        internal static string VRPatcherPath => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        internal static string ManagedPath => Paths.ManagedPath;
        internal static string PluginsPath => Path.Combine(ManagedPath, "../Plugins/x86_64");
        internal static string SubsystemsPath => Path.Combine(ManagedPath, "../UnitySubsystems");
        internal static string OpenXRSubsystemsPath => Path.Combine(SubsystemsPath, "UnityOpenXR");

        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("VRDependenciesPatcher");
        

        /// <summary>
        /// Called from BepInEx while patching, our entry point for patching.
        /// Do not change the method name as it is identified by BepInEx. Method must remain public.
        /// </summary>
        [Obsolete("Should not be used!", true)]
        public static void Initialize()
        {
            try
            {
                var installer = new NativeFileInstaller(Path.GetFullPath(Path.Combine(ManagedPath, "..")),
                    message => Logger.LogInfo(message));
                installer.Install(new Dictionary<string, byte[]>
                {
                    { "UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json", Properties.Resources.UnitySubsystemsManifest },
                    { "Plugins/x86_64/UnityOpenXR.dll", Properties.Resources.UnityOpenXR },
                    { "Plugins/x86_64/openxr_loader.dll", Properties.Resources.openxr_loader }
                });
            }
            catch (Exception error)
            {
                Logger.LogError("Native dependencies were not fully installed: " + error.Message +
                    " Close the game and review the .vrmod-native ledger with restore-native.py before retrying.");
            }
        }

        /// <summary>
        /// For BepInEx to identify your patcher as a patcher, it must match the patcher contract as outlined in the BepInEx docs:
        /// https://bepinex.github.io/bepinex_docs/v5.0/articles/dev_guide/preloader_patchers.html#patcher-contract
        /// It must contain a list of managed assemblies to patch as a public static <see cref="IEnumerable{T}"/> property named TargetDLLs
        /// </summary>
        [Obsolete("Should not be used!", true)]
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        /// <summary>
        /// For BepInEx to identify your patcher as a patcher, it must match the patcher contract as outlined in the BepInEx docs:
        /// https://bepinex.github.io/bepinex_docs/v5.0/articles/dev_guide/preloader_patchers.html#patcher-contract
        /// It must contain a public static void method named Patch which receives an <see cref="AssemblyDefinition"/> argument,
        /// which patches each of the target assemblies in the TargetDLLs list.
        /// 
        /// We don't actually need to patch any of the managed assemblies, so we are providing an empty method here.
        /// </summary>
        /// <param name="ad"></param>
        [Obsolete("Should not be used!", true)]
        public static void Patch(AssemblyDefinition ad) { }
    }
}
