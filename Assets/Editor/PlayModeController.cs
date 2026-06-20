using UnityEditor;
using System.IO;
using UnityEngine;

namespace UnityRoyale
{
    [InitializeOnLoad]
    public static class PlayModeController
    {
        private static readonly string flagPath = "PlayModeFlag.txt";

        static PlayModeController()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (File.Exists(flagPath))
            {
                try
                {
                    string content = File.ReadAllText(flagPath).Trim();
                    if (content == "play")
                    {
                        if (!EditorApplication.isPlaying && !EditorApplication.isCompiling)
                        {
                            Debug.Log("[PlayModeController] Flag detected: starting Play Mode.");
                            File.WriteAllText(flagPath, "playing");
                            EditorApplication.isPlaying = true;
                        }
                    }
                    else if (content == "stop")
                    {
                        if (EditorApplication.isPlaying)
                        {
                            Debug.Log("[PlayModeController] Flag detected: stopping Play Mode.");
                            File.WriteAllText(flagPath, "stopped");
                            EditorApplication.isPlaying = false;
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Ignore transient file sharing violations
                }
            }
        }
    }
}
