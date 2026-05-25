using System;
using System.Diagnostics;
using System.Threading;
using Celeste.Mod.AudioSplitter.Audio;
using Celeste.Mod.AudioSplitter.Extensions;
using Celeste.Mod.AudioSplitter.UI;
using Celeste.Mod.AudioSplitter.Utility;
using FMOD.Studio;
using CelesteAudio = global::Celeste.Audio;

namespace Celeste.Mod.AudioSplitter.Module
{
    public class AudioSplitterModule : EverestModule
    {
        public static AudioSplitterModule Instance { get; private set; }

        public override Type SettingsType => typeof(AudioSplitterModuleSettings);
        public static AudioSplitterModuleSettings Settings => (AudioSplitterModuleSettings)Instance._Settings;

        public override Type SessionType => typeof(AudioSplitterModuleSession);
        public static AudioSplitterModuleSession Session => (AudioSplitterModuleSession)Instance._Session;

        public AudioDuplicator SFXDuplicator { get; private set; } = new AudioDuplicator();
        public AudioDuplicator MusicDuplicator { get; private set; } = new AudioDuplicator();
        public OutputDeviceManager DeviceManager { get; private set; } = new OutputDeviceManager();

        public bool Enabled => SFXDuplicator.Initialized && MusicDuplicator.Initialized;

        private AudioSplitterModulePresenter presenter = new();
        private LoadingMessage loadingMessage = null;

        public AudioSplitterModule()
        {
            Instance = this;
#if DEBUG
            // debug builds use verbose logging
            Logger.SetLogLevel(nameof(AudioSplitterModule), LogLevel.Verbose);
#else
            // release builds use info logging to reduce spam in log files
            Logger.SetLogLevel(nameof(AudioSplitterModule), LogLevel.Info);
#endif
        }

        public override void Load()
        {
#if DEBUG
            bool waitForDebugger = false;
            while (waitForDebugger && !Debugger.IsAttached)
                continue;
#endif

            HookAttribute.Invoke(typeof(ApplyOnLoadAttribute));
        }

        public override void Unload()
        {
            
            HookAttribute.Invoke(typeof(RemoveOnUnloadAttribute));

            DeviceManager.Terminate();
            SFXDuplicator.Terminate();
            MusicDuplicator.Terminate();
        }

        public override void Initialize()
        {
            DeviceManager.Initialize();
            DeviceManager.OnListUpdate += (_) => { ConfigureSystemDevices(); };
        }

        public override void LoadContent(bool firstLoad)
        {
            if (firstLoad)
            {
                loadingMessage = new(Celeste.Instance, default, new(20f, LoadingMessage.UI_HEIGHT - 20f));
            }
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot)
        {
            CreateModMenuSectionHeader(menu, inGame, snapshot);

            var view = new AudioSplitterModuleView();
            presenter.Attach(view);
            view.AddTo(menu, inGame);

            menu.OnClose += () => { presenter.Detach(); };
        }

        public LiveData<bool> LoadingAudioDuplication = new(false);

        public void ToggleAudioDuplicator()
        {
            LoadingAudioDuplication.Value = true;
            if (!SFXDuplicator.Initialized)
                SFXDuplicator.Initialize();
            else
                SFXDuplicator.Terminate();
            
            if (!MusicDuplicator.Initialized)
                MusicDuplicator.Initialize();
            else
                MusicDuplicator.Terminate();
            LoadingAudioDuplication.Value = false;

            ConfigureSystemDevices();
            global::Celeste.Settings.Instance.ApplyVolumes();
        }

        public void ToggleAudioDuplicatorInThread()
        {
            Thread thread = new(
                () => {
                    ToggleAudioDuplicator();
                }
            );
            thread.Start();
        }

        public void ConfigureSystemDevices()
        {
            DeviceManager.SetDevice(Settings.AudioOutputDevice, CelesteAudio.System);
            
            if (!Enabled)
                return;
            
            DeviceManager.SetDevice(Settings.SFXOutputDevice, SFXDuplicator.System);
            DeviceManager.SetDevice(Settings.MusicOutputDevice, MusicDuplicator.System);
        }

        private void UpdateLoadingMessageText()
        {
            var dialog = !Enabled ? "LOADING_MESSAGE" : "UNLOADING_MESSAGE";
            loadingMessage.Label = Dialog.Clean($"AUDIOSPLITTER_{dialog}");
        }

        private void ShowLoadingMessageOnLoading(bool loading)
        {
            if (loading)
            {
                UpdateLoadingMessageText();
                loadingMessage.Add();
            }
            else
            {
                loadingMessage.Remove();
            }
        }

        internal static class AudioSplitterModuleHooks
        {
            [ApplyOnLoad]
            public static void Apply()
            {
                On.Celeste.Audio.Init += OnAudioInit;
                On.Celeste.Audio.Unload += OnAudioUnload;
                On.Celeste.Audio.VCAVolume += OnAudioVCAVolume;

                On.Celeste.OuiMainMenu.Update += DisableExitWhileLoading;

                On.Celeste.Overworld.ctor += Overworld_ctor;
                On.Celeste.Settings.ApplyLanguage += Settings_ApplyLanguage;
            }

            public static void Settings_ApplyLanguage(On.Celeste.Settings.orig_ApplyLanguage orig, Settings self)
            {
                orig(self);
                Instance.UpdateLoadingMessageText();
            }

            [RemoveOnUnload]
            public static void Remove()
            {
                On.Celeste.Audio.Init -= OnAudioInit;
                On.Celeste.Audio.Unload -= OnAudioUnload;
                On.Celeste.Audio.VCAVolume -= OnAudioVCAVolume;

                On.Celeste.OuiMainMenu.Update -= DisableExitWhileLoading;
                
                On.Celeste.Overworld.ctor -= Overworld_ctor;
            }

            public static void OnAudioInit(On.Celeste.Audio.orig_Init orig)
            {
                orig();

                if (Settings.EnableOnStartup)
                    Instance.ToggleAudioDuplicatorInThread();
            }

            public static void OnAudioUnload(On.Celeste.Audio.orig_Unload orig)
            {
                orig();
                Instance.SFXDuplicator.Terminate();
                Instance.MusicDuplicator.Terminate();
            }

            public static float OnAudioVCAVolume(On.Celeste.Audio.orig_VCAVolume orig, string path, float? volume = null)
            {
                // If duplicator is not active, just control the original VCA
                if (!Instance.Enabled)
                    return orig(path, volume);

                // Forward sounds to original and music to duplicator
                if (path == "vca:/gameplay_sfx" || path == "vca:/ui_sfx")
                {
                    Instance.SFXDuplicator.VCAVolume(path, 1f);
                    Instance.MusicDuplicator.VCAVolume(path, 0f);
                }
                else if (path == "vca:/music")
                {
                    Instance.MusicDuplicator.VCAVolume(path, 1f);
                    Instance.SFXDuplicator.VCAVolume(path, 0f);
                }
                
                // Volume is controlled normally on the default system
                return orig(path, volume);
            }

            public static void DisableExitWhileLoading(On.Celeste.OuiMainMenu.orig_Update orig, OuiMainMenu self)
            {
                var exitButton = self.Buttons.Find(
                    b => b.GetType() == typeof(MainMenuSmallButton)
                    && ((MainMenuSmallButton)b).LabelName == "menu_exit"
                );
                if (exitButton != default(MenuButton))
                {
                    if (Instance.LoadingAudioDuplication.Value)
                    {
                        if (!exitButton.IsDisabled())
                            exitButton.Disable();
                    } else
                    {
                        if (exitButton.IsDisabled())
                            exitButton.Enable();
                    }
                }

                orig(self);
            }

            public static void Overworld_ctor(On.Celeste.Overworld.orig_ctor orig, Overworld self, OverworldLoader loader)
            {
                Instance.LoadingAudioDuplication.Observe(Instance.ShowLoadingMessageOnLoading);
                orig(self, loader);
            }
        }
    }
}