using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Celeste.Mod.AudioSplitter.Module;
using Celeste.Mod.AudioSplitter.Utility;
using Celeste.Mod.Core;
using FMOD;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.AudioSplitter.Audio
{
    /// <summary>
    /// Fully duplicates all audio from original <see cref="CelesteNamespace.Audio">Celeste.Audio<see/> FMOD system
    /// into a AudioDuplicate's FMOD system
    /// </summary>
    /// 
    /// <remarks>
    /// One FMOD system can only play to a single audio device so we need more systems to play to more devices  
    /// https://qa.fmod.com/t/playing-different-audios-simultaneously-through-different-output-devices/18461/2
    /// 
    /// Each instance of AudioDuplicate is responsible for hooking the necessary methods for full audio replication
    /// </remarks>
    public class AudioDuplicator
    {
        public static List<AudioDuplicator> Instances { get; private set; } = new();
        public static List<AudioDuplicator> ReadyInstances => Instances.Where(x => x.Ready).ToList();
        public static List<AudioDuplicator> InitializedInstances => Instances.Where(x => x.Initialized).ToList();

        private FMOD.Studio.System system = null;

        private BankCache bankCache = null;
        private EventCache eventCache = null;
        private InstanceDuplicator instanceDuplicator = null;

        private RecursionLocker locker = new();

        public bool Ready { get; private set; } = false;
        public bool Initialized { get; private set; } = false;
        public FMOD.Studio.System System => system;

        public AudioDuplicator() => Instances.Add(this);
        ~AudioDuplicator() => Instances.Remove(this);

        /// <summary>
        /// Load the audio data and apply the necessary hooks
        /// </summary>
        public void Initialize()
        {
            if (Initialized)
                return;

            FMOD.Studio.System.create(out system).CheckFMOD();
            system.initialize(1024, FMOD.Studio.INITFLAGS.NORMAL, FMOD.INITFLAGS.NORMAL, IntPtr.Zero).CheckFMOD();

            var attributes = new FMOD.Studio._3D_ATTRIBUTES
            {
                forward = new VECTOR { x = 0f, y = 0f, z = 1f },
                up = new VECTOR { x = 0f, y = 1f, z = 0f },
                position = new VECTOR { x = 0f, y = 0f, z = -345f },
            };
            system.setListenerAttributes(0, attributes).CheckFMOD();

            // TODO: Load banks in a different thread and add a cool loading animation
            bankCache = new(system);
            eventCache = new(system);
            instanceDuplicator = new(system);

            bankCache.LoadUnloadedBanks();
            eventCache.LoadUsedDescriptions();
            instanceDuplicator.Initialize();

            Ready = true;
            Initialized = true;
        }

        /// <summary>
        /// Unload the audio data and remove the hooks
        /// </summary>
        public void Terminate()
        {
            if (!Initialized)
                return;

            Ready = false;

            bankCache.UnloadBanks();
            eventCache.Clear();

            instanceDuplicator.Terminate();
            instanceDuplicator.Clear();

            system.release();
            system = null;

            Initialized = false;
        }

        public void Update()
        {
            if (system != null && Ready)
            {
                system.update().CheckFMOD();
            }
        }

        public float VCAVolume(string path, float? newVolume = null)
        {
            float volume = 1f;
            if (system.getVCA(path, out VCA vca) == RESULT.OK)
            {
                if (newVolume != null)
                    vca.setVolume(newVolume.Value);
                vca.getVolume(out volume, out _);
            }
            else
            {
                Logger.Error(nameof(AudioSplitterModule), "Failed to get VCA for VCAVolume");
            }
            return volume;
        }

        public void BusMute(string path, bool? mute)
        {
            if (mute != null && system != null && system.getBus(path, out Bus bus) == RESULT.OK)
            {
                bus.setMute(mute.Value);
            }
        }

        public void BusPause(string path, bool? pause)
        {
            if (pause != null && system != null && system.getBus(path, out Bus bus) == RESULT.OK)
            {
                bus.setPaused(pause.Value);
            }
        }

        public void BusStopAll(string path, bool immediate)
        {
            if (system != null && system.getBus(path, out Bus bus) == RESULT.OK)
            {
                bus.stopAllEvents(immediate ? STOP_MODE.IMMEDIATE : STOP_MODE.ALLOWFADEOUT);
            }
        }

        public void SetListenerPosition(Vector3 forward, Vector3 up, Vector3 position)
        {
            FMOD.Studio._3D_ATTRIBUTES attributes = new FMOD.Studio._3D_ATTRIBUTES
            {
                position = new VECTOR { x = position.X, y = position.Y, z = position.Z },
                forward = new VECTOR { x = forward.X, y = forward.Y, z = forward.Z },
                up = new VECTOR { x = up.X, y = up.Y, z = up.Z },
            };
            system.setListenerAttributes(0, attributes);
        }

        internal static class AudioDuplicatorHooks
        {
            [ApplyOnLoad]
            public static void Apply()
            {
                On.Celeste.Audio.Update += OnAudioUpdate;

                On.Celeste.Audio.GetEventDescription += OnAudioGetEventDescription;
                On.Celeste.Audio.ReleaseUnusedDescriptions += OnAudioReleaseUnusedDescriptions;

                On.Celeste.Audio.BusMuted += OnAudioBusMuted;
                On.Celeste.Audio.BusPaused += OnAudioBusPaused;
                On.Celeste.Audio.BusStopAll += OnAudioBusStopAll;
                On.Celeste.Audio.SetListenerPosition += OnAudioSetListenerPosition;
            }

            [RemoveOnUnload]
            public static void Remove()
            {
                On.Celeste.Audio.Update -= OnAudioUpdate;

                On.Celeste.Audio.GetEventDescription -= OnAudioGetEventDescription;
                On.Celeste.Audio.ReleaseUnusedDescriptions -= OnAudioReleaseUnusedDescriptions;

                On.Celeste.Audio.BusMuted -= OnAudioBusMuted;
                On.Celeste.Audio.BusPaused -= OnAudioBusPaused;
                On.Celeste.Audio.BusStopAll -= OnAudioBusStopAll;
                On.Celeste.Audio.SetListenerPosition -= OnAudioSetListenerPosition;
            }

            public static void OnAudioUpdate(On.Celeste.Audio.orig_Update orig)
            {
                orig();
                foreach (var instance in ReadyInstances)
                    instance.Update();
            }

            public static EventDescription OnAudioGetEventDescription(On.Celeste.Audio.orig_GetEventDescription orig, string path)
            {
                foreach (var instance in ReadyInstances)
                    instance.eventCache.LoadEventDescription(path);
                return orig(path);
            }

            public static void OnAudioReleaseUnusedDescriptions(On.Celeste.Audio.orig_ReleaseUnusedDescriptions orig)
            {
                if (CoreModule.Settings.UnloadUnusedAudio)
                {
                    foreach (var instance in ReadyInstances)
                        instance.eventCache.ReleaseUnusedDescriptions();
                }
                orig();
            }

            public static bool OnAudioBusMuted(On.Celeste.Audio.orig_BusMuted orig, string path, bool? mute = null)
            {
                foreach (var instance in ReadyInstances)
                    instance.BusMute(path, mute);
                return orig(path, mute);
            }

            public static bool OnAudioBusPaused(On.Celeste.Audio.orig_BusPaused orig, string path, bool? pause = null)
            {
                foreach (var instance in ReadyInstances)
                    instance.BusPause(path, pause);
                return orig(path, pause);
            }

            public static void OnAudioBusStopAll(On.Celeste.Audio.orig_BusStopAll orig, string path, bool immediate = false)
            {
                foreach (var instance in ReadyInstances)
                    instance.BusStopAll(path, immediate);
                orig(path, immediate);
            }

            public static void OnAudioSetListenerPosition(On.Celeste.Audio.orig_SetListenerPosition orig, Vector3 forward, Vector3 up, Vector3 position)
            {
                foreach (var instance in ReadyInstances)
                    instance.SetListenerPosition(forward, up, position);
                orig(forward, up, position);
            }
        }
    }
}
