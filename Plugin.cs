using BepInEx;
using BepInEx.Logging;
using System.IO;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("Detalhes.Deadheim", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Detalhes.Combat", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.valheimmontarias.mod";
        public const string Name = "ValheimMontarias";
        public const string Version = "0.3.18";

        internal static ManualLogSource Log;
        private Harmony _harmony;
        private bool _consoleWasEnabled;
        private bool _consoleWasVisible;
        private static readonly ConfigSync ConfigSync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version
        };

        /// <summary>
        /// The admin bit ServerSync received from the server. On a dedicated server the vanilla
        /// LocalPlayerIsAdminOrHost is not reliable on the client (NpcValheim found the same),
        /// while the server already told ServerSync. Only decides what the menu shows: every
        /// admin action is checked again on the server (RiderServer.IsAdmin).
        /// </summary>
        internal static bool LocalIsServerSyncAdmin =>
            (ZNet.instance != null && ZNet.instance.IsServer()) ||
            (ConfigSync.InitialSyncDone && ConfigSync.IsAdmin);

        private void Awake()
        {
            Log = Logger;
            MountSettings.Init(this, ConfigSync);
            WatchConfigFile();
            MountMenu.EnsureCreated();
            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll();
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"ValheimMontarias: PatchAll failed, applying patches one by one: {e.Message}");
                foreach (var type in typeof(Plugin).Assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                        continue;
                    try
                    {
                        _harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (System.Exception pe)
                    {
                        Log.LogWarning($"ValheimMontarias: skipped patch {type.Name}: {pe.Message}");
                    }
                }
            }

            if (Localization.instance != null)
                Patches.Localization_SetupLanguage_Patch.Apply(Localization.instance);
            Log.LogInfo($"{Name} v{Version} loaded");
        }

        private FileSystemWatcher _configWatcher;

        /// <summary>
        /// Edits to the cfg on a running dedicated server are read back (and ServerSync sends
        /// them to every client), so SelaRotacao and the rest no longer need a restart. Server
        /// only: on a client the server's values win, and reloading the local file would only
        /// put stale values back.
        /// </summary>
        private void WatchConfigFile()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                return;
            var path = Config.ConfigFilePath;
            _configWatcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                SynchronizingObject = ThreadingHelper.SynchronizingObject,
                EnableRaisingEvents = true
            };
            _configWatcher.Changed += ReloadConfig;
            _configWatcher.Created += ReloadConfig;
            _configWatcher.Renamed += ReloadConfig;
        }

        private void ReloadConfig(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(Config.ConfigFilePath))
                return;
            try
            {
                Config.Reload();
                Log.LogInfo("ValheimMontarias: config reloaded from disk");
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"ValheimMontarias: could not reload config: {ex.Message}");
            }
        }

        private void Update()
        {
            _consoleWasVisible = Console.IsVisible();
            _consoleWasEnabled = Console.instance != null && Console.instance.IsConsoleEnabled();
            if (Input.GetKeyDown(KeyCode.F8))
                AdminMenu.Toggle();
            AdminMenu.Tick();
            JavaliControl.Tick(Player.m_localPlayer);
        }

        private void LateUpdate()
        {
            RescueConsole();
            var player = Player.m_localPlayer;
            if (player != null && player.gameObject.activeInHierarchy
                && player.GetComponent<RiderVisualFix>() == null)
                player.gameObject.AddComponent<RiderVisualFix>();
        }

        private void OnGUI()
        {
            AdminMenu.Draw();
        }

        private void RescueConsole()
        {
            bool pressed = Input.GetKeyDown(KeyCode.F5) || Access.ButtonDown("Console");
            if (!pressed) return;

            Console.SetConsoleEnabledForThisSession();
            if (Console.IsVisible() != _consoleWasVisible)
                return;
            if (_consoleWasEnabled && _consoleWasVisible)
                return;

            OpenConsoleWindow();
        }

        private static void OpenConsoleWindow()
        {
            var inst = Console.instance;
            if (inst == null) return;
            var chat = Access.Get(inst, "m_chatWindow") as RectTransform;
            if (chat == null) return;

            var t = chat.transform;
            while (t != null)
            {
                t.gameObject.SetActive(true);
                t = t.parent;
            }

            chat.gameObject.SetActive(true);
            Access.Set(inst, "m_focused", true);
            Access.Call(Access.Get(inst, "m_input"), "ActivateInputField");
        }

        private void OnDestroy()
        {
            _configWatcher?.Dispose();
            _harmony?.UnpatchSelf();
        }
    }
}
