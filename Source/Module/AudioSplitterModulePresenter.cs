using System;
using System.Collections.Generic;
using Celeste.Mod.AudioSplitter.Audio;
using Celeste.Mod.AudioSplitter.UI;

namespace Celeste.Mod.AudioSplitter.Module
{
    public class AudioSplitterModulePresenter
    {
        internal static AudioSplitterModule Module => AudioSplitterModule.Instance;
        internal static AudioSplitterModuleSettings Settings => AudioSplitterModule.Settings;

        private AudioSplitterModuleView view = null;
        
        public AudioSplitterModulePresenter() { }

        public void Attach(AudioSplitterModuleView view)
        {
            this.view = view;

            InitializeElements();
            AddEventsToElements();

            Module.DeviceManager.OnListUpdate += OnDeviceListUpdate;
            Module.LoadingAudioDuplication.Observe(OnLoadingAudioDuplicatorUpdate);
        }

        public void Detach()
        {
            Module.DeviceManager.OnListUpdate -= OnDeviceListUpdate;
            Module.LoadingAudioDuplication.StopObserving(OnLoadingAudioDuplicatorUpdate);
            
            view = null;
        }

        private void InitializeElements()
        {
            UpdateToggleDuplicatorLabel();
            ToggleDropdownVisibility();

            // Important: Set option first, update devices after!
            view.AudioDeviceDropdown.CurrentOption = new(Settings.AudioOutputDevice.Name, Settings.AudioOutputDevice);
            view.SFXDeviceDropdown.CurrentOption = new(Settings.SFXOutputDevice.Name, Settings.SFXOutputDevice);
            view.MusicDeviceDropdown.CurrentOption = new(Settings.MusicOutputDevice.Name, Settings.MusicOutputDevice);

            UpdateDropdownDevices(view.AudioDeviceDropdown, Module.DeviceManager.Devices);
            UpdateDropdownDevices(view.SFXDeviceDropdown, Module.DeviceManager.Devices);
            UpdateDropdownDevices(view.MusicDeviceDropdown, Module.DeviceManager.Devices);

            view.EnableOnStartupOnOff.Index = (Settings.EnableOnStartup ? 1 : 0);
        }

        private void AddEventsToElements()
        {
            view.AudioDeviceDropdown.Change((device) =>
            {
                Settings.AudioOutputDevice = device;
                Module.DeviceManager.SetDevice(Settings.AudioOutputDevice, global::Celeste.Audio.System);
            });
            view.MusicDeviceDropdown.Change((device) =>
            {
                Settings.MusicOutputDevice = device;
                Module.DeviceManager.SetDevice(Settings.MusicOutputDevice, Module.MusicDuplicator.System);
            });
            view.SFXDeviceDropdown.Change((device) =>
            {
                Settings.SFXOutputDevice = device;
                Module.DeviceManager.SetDevice(Settings.SFXOutputDevice, Module.SFXDuplicator.System);
            });

            view.ToggleDuplicatorButton.Pressed(() =>
            {
                Module.ToggleAudioDuplicatorInThread();
            });

            view.ReloadDevicesButton.Pressed(() => { Module.DeviceManager.ReloadDeviceList(); });
            view.EnableOnStartupOnOff.Change((value) => { Settings.EnableOnStartup = value; });
        }

        private void UpdateToggleDuplicatorLabel()
        {
            var toggleDialog = Module.Enabled ? "DISABLE_DUPLICATE" : "ENABLE_DUPLICATE";
            view.ToggleDuplicatorButton.Label = Dialog.Clean($"MODOPTIONS_AUDIOSPLITTER_{toggleDialog}");
        }

        private void ToggleDropdownVisibility()
        {
            view.AudioDeviceDropdown.Visible = true;
            view.SFXDeviceDropdown.Visible = Module.Enabled;
            view.MusicDeviceDropdown.Visible = Module.Enabled;
        }

        private void UpdateDropdownDevices(DropdownMenu<OutputDeviceInfo> dropdownMenu, List<OutputDeviceInfo> devices)
        {
            // Add devices
            dropdownMenu.Clear();
            dropdownMenu.Add(Dialog.Clean("MODOPTIONS_AUDIOSPLITTER_DEFAULT_DEVICE"), OutputDeviceInfo.DefaultDevice);
            foreach (var info in devices)
                dropdownMenu.Add(info.Name, info);

            // Update option index
            OutputDeviceInfo device = dropdownMenu.CurrentOption.Value;
            var index = devices.IndexOf(device);
            if (index != -1)
                dropdownMenu.OptionIndex = GetDeviceIndex(device);
        }

        private int GetDeviceIndex(OutputDeviceInfo deviceInfo)
        {
            return deviceInfo.Index + (deviceInfo != OutputDeviceInfo.DefaultDevice ? 1 : 0); 
        }

        private void OnDeviceListUpdate(List<OutputDeviceInfo> devices)
        {
            UpdateDropdownDevices(view.AudioDeviceDropdown, devices);
            UpdateDropdownDevices(view.SFXDeviceDropdown, devices);
            UpdateDropdownDevices(view.MusicDeviceDropdown, devices);
        }

        private void OnLoadingAudioDuplicatorUpdate(bool loading)
        {
            if (view != null)
            {
                if (loading)
                {
                    view.ToggleDuplicatorButton.Disabled = true;
                }
                else
                {
                    ToggleDropdownVisibility();
                    UpdateToggleDuplicatorLabel();
                    view.ToggleDuplicatorButton.Disabled = false;
                }
            }
        }
    }
}
