using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using System.IO;
using System.Diagnostics;

namespace UnityRoyale
{
    public class SetupMLAgent : MonoBehaviour
    {
        [MenuItem("Tools/ML-Agents/1. Setup Scene (Assign Components & Refs)")]
        public static void Setup()
        {
            // 1. Load scene
            string scenePath = "Assets/Scenes/Main.unity";
            var scene = EditorSceneManager.OpenScene(scenePath);
            if (!scene.IsValid())
            {
                UnityEngine.Debug.LogError("Failed to open Main scene at " + scenePath);
                return;
            }

            // 2. Find GameManager GameObject
            GameObject gameManagerGO = GameObject.Find("GameManager");
            if (gameManagerGO == null)
            {
                GameManager gm = FindObjectOfType<GameManager>();
                if (gm != null) gameManagerGO = gm.gameObject;
            }

            if (gameManagerGO == null)
            {
                UnityEngine.Debug.LogError("Could not find GameManager GameObject in the scene!");
                return;
            }

            // 2.5 Clean up missing scripts and old/incorrect Agent components
            int removedMissing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameManagerGO);
            if (removedMissing > 0)
            {
                UnityEngine.Debug.Log($"Removed {removedMissing} missing script components from GameManager.");
            }

            // Remove any Agent component that is not MLPlayerAgent (e.g. generic Agent or MLOpponentAgent)
            Component[] components = gameManagerGO.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                
                // If the component inherits from Unity.MLAgents.Agent but is not MLPlayerAgent
                if (comp is Agent && comp.GetType() != typeof(MLPlayerAgent))
                {
                    UnityEngine.Debug.Log($"Removing old/incorrect Agent component of type: {comp.GetType().FullName}");
                    Undo.DestroyObjectImmediate(comp);
                }
            }

            // 3. Get or Add MLPlayerAgent component
            MLPlayerAgent agent = gameManagerGO.GetComponent<MLPlayerAgent>();
            if (agent == null)
            {
                agent = gameManagerGO.AddComponent<MLPlayerAgent>();
                UnityEngine.Debug.Log("Added MLPlayerAgent component.");
            }

            // 4. Get or Add BehaviorParameters component
            BehaviorParameters bp = gameManagerGO.GetComponent<BehaviorParameters>();
            if (bp == null)
            {
                bp = gameManagerGO.AddComponent<BehaviorParameters>();
                UnityEngine.Debug.Log("Added BehaviorParameters component.");
            }
            // Setup Behavior Parameters values
            bp.BehaviorName = "MLPlayer";
            bp.BrainParameters.VectorObservationSize = 40;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = new ActionSpec(2, new int[] { 4 }); // 2 continuous, 1 discrete branch with size 4

            // 5. Get or Add DecisionRequester component
            DecisionRequester dr = gameManagerGO.GetComponent<DecisionRequester>();
            if (dr == null)
            {
                dr = gameManagerGO.AddComponent<DecisionRequester>();
                UnityEngine.Debug.Log("Added DecisionRequester component.");
            }
            dr.DecisionPeriod = 5;

            // 6. Set References
            GameManager gameManager = gameManagerGO.GetComponent<GameManager>();
            agent.gameManager = gameManager;
            gameManager.mlAgent = agent;

            // Assign NavMeshSurface reference if not set
            if (gameManager.navMesh == null)
            {
                MonoBehaviour navSurface = null;
                foreach (var mono in Object.FindObjectsOfType<MonoBehaviour>())
                {
                    if (mono != null && mono.GetType().Name == "NavMeshSurface")
                    {
                        navSurface = mono;
                        break;
                    }
                }
                if (navSurface != null)
                {
                    gameManager.navMesh = navSurface;
                    UnityEngine.Debug.Log("Assigned NavMeshSurface reference to GameManager.");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("NavMeshSurface not found in scene! Make sure a NavMesh Surface GameObject is present.");
                }
            }

            // Load Player deck (BaseDeck) from assets
            DeckData playerDeckAsset = AssetDatabase.LoadAssetAtPath<DeckData>("Assets/GameData/1 Decks/BaseDeck.asset");
            if (playerDeckAsset != null)
            {
                agent.playerDeck = playerDeckAsset;
                UnityEngine.Debug.Log("Assigned BaseDeck (player deck) asset reference.");
            }
            else
            {
                UnityEngine.Debug.LogWarning("Could not find BaseDeck asset at Assets/GameData/1 Decks/BaseDeck.asset. Please assign manually.");
            }

            // 7. Mark scene as dirty and save
            EditorUtility.SetDirty(gameManagerGO);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            UnityEngine.Debug.Log("ML-Agent setup completed and scene saved successfully!");
        }

        [MenuItem("Tools/ML-Agents/2. Install Python Dependencies (pip)")]
        public static void InstallDependencies()
        {
            UnityEngine.Debug.Log("Starting Python ML-Agents package installation...");
            
            // Runs pip install in a separate cmd window
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/k py -3.10 -m pip install mlagents==1.1.0"; // Use /k so the command prompt remains open to see results
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = true;

            try
            {
                Process.Start(startInfo);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("Failed to start installation command: " + e.Message);
            }
        }

        [MenuItem("Tools/ML-Agents/3. Start ML-Agents Training Server")]
        public static void StartTraining()
        {
            // 1. Create trainer config if not exists
            string configDir = Path.Combine(Application.dataPath, "../config");
            string configFile = Path.Combine(configDir, "trainer_config.yaml");

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            if (!File.Exists(configFile))
            {
                string defaultConfig = @"behaviors:
  MLPlayer:
    trainer_type: ppo
    hyperparameters:
      batch_size: 256
      buffer_size: 4096
      learning_rate: 3.0e-4
      beta: 5.0e-3
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      normalize: true
      hidden_units: 256
      num_layers: 2
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    max_steps: 500000
    time_horizon: 256
    summary_freq: 10000";

                File.WriteAllText(configFile, defaultConfig);
                UnityEngine.Debug.Log("Created default ML-Agents config at " + configFile);
            }

            UnityEngine.Debug.Log("Launching ML-Agents training command in command prompt...");

            // 2. Start training server in cmd.exe
            // Set working directory to the project root
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/k py -3.10 -m mlagents.trainers.learn config/trainer_config.yaml --run-id=UnityRoyaleAgent --force";
            startInfo.WorkingDirectory = projectRoot;
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = true;

            try
            {
                Process.Start(startInfo);
                UnityEngine.Debug.Log("Training window launched! Please click 'Play' in the Unity Editor to start training.");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("Failed to start training server: " + e.Message);
            }
        }

        [MenuItem("Tools/ML-Agents/4. Clean Training Results (Delete Checkpoints)")]
        public static void CleanResults()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string resultsPath = Path.Combine(projectRoot, "results");

            if (Directory.Exists(resultsPath))
            {
                try
                {
                    Directory.Delete(resultsPath, true);
                    UnityEngine.Debug.Log("Successfully deleted training results directory: " + resultsPath);
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError("Failed to delete results directory: " + e.Message);
                }
            }
            else
            {
                UnityEngine.Debug.Log("Results directory does not exist. No cleanup needed.");
            }
        }
    }
}
