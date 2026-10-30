using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace MMO.Client
{
    /// <summary>
    /// The client's one audio endpoint: a fixed set of 3D sources, a hard voice cap, and the
    /// mixer buses the settings panel drives.
    ///
    /// <para>
    /// Every impact asks for a voice. When all sources are busy, <see cref="AudioVoiceBudget"/>
    /// decides which sounds survive; losers are stopped instead of stacked, so a 50-player fight
    /// cannot blow out the listener's speakers or allocate a source per hit. Sources are built
    /// once by <see cref="Configure"/> and reused for the process lifetime.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        public const string MixerResourcePath = "Audio/MMOAudioMixer";
        public const int DefaultMaxVoices = 24;

        public const string MasterVolumeParameter = "MasterVolume";
        public const string MusicVolumeParameter = "MusicVolume";
        public const string SfxVolumeParameter = "SfxVolume";
        public const string VoiceVolumeParameter = "VoiceVolume";

        /// <summary>The one service in this process, or null before the local HUD starts.</summary>
        public static AudioService Instance { get; private set; }

        private AudioMixer _mixer;
        private AudioMixerGroup _sfxGroup;
        private AudioListener _listener;
        private SettingsModel _settings = SettingsModel.Defaults();

        private VoiceSlot[] _slots;
        private VoiceRequest[] _requests;
        private int _maxVoices = DefaultMaxVoices;
        private Transform _voicesRoot;

        /// <summary>Voices this service may use at once.</summary>
        public int MaxVoices => _maxVoices;

        /// <summary>Voices currently holding a source.</summary>
        public int ActiveVoices
        {
            get
            {
                int active = 0;
                if (_slots == null)
                    return active;

                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Occupied && _slots[i].Source != null && _slots[i].Source.isPlaying)
                        active++;
                }

                return active;
            }
        }

        /// <summary>Ensures a service exists under <paramref name="parent"/>; idempotent.</summary>
        public static AudioService Ensure(Transform parent)
        {
            if (Instance != null)
                return Instance;

            var root = new GameObject("AudioService");
            if (parent != null)
                root.transform.SetParent(parent, false);

            return root.AddComponent<AudioService>();
        }

        /// <summary>
        /// Builds or rebuilds the source pool. Public so a test or a future scene bootstrap can
        /// supply its own mixer instead of the Resources fallback.
        /// </summary>
        public void Configure(AudioMixer mixer, int maxVoices)
        {
            _mixer = mixer;
            _maxVoices = Mathf.Clamp(maxVoices, 1, 64);
            _sfxGroup = FindGroup("SFX");

            BuildVoices();
            ApplySettings(_settings);
        }

        /// <summary>Plays the placeholder impact at <paramref name="position"/> if a voice is free.</summary>
        public void PlayImpact(Vector3 position, int priority = 0)
            => Play(PlaceholderAudioClips.Impact, position, priority, volume: 1f);

        /// <summary>
        /// Requests one voice. Returns false when the budget rejected the sound, so a caller can
        /// tell "did not fit" from "played".
        /// </summary>
        public bool Play(AudioClip clip, Vector3 position, int priority = 0, float volume = 1f)
        {
            if (clip == null || _slots == null || _slots.Length == 0)
                return false;

            var request = new VoiceRequest(DistanceSquared(position), priority, volume);
            int target = FindFreeSlot();

            if (target < 0)
            {
                BuildRequests(request);
                IReadOnlyList<int> selected = AudioVoiceBudget.Select(_requests, _maxVoices);
                int newIndex = _requests.Length - 1;

                if (!AudioVoiceBudget.Contains(selected, newIndex))
                    return false;

                for (int i = 0; i < _slots.Length; i++)
                {
                    if (!_slots[i].Occupied || AudioVoiceBudget.Contains(selected, i))
                        continue;

                    Stop(i);
                    target = i;
                    break;
                }

                if (target < 0)
                    return false;
            }

            PlayOn(target, clip, position, volume, request);
            return true;
        }

        /// <summary>Pushes master/music/SFX/voice levels into the mixer's exposed parameters.</summary>
        public void ApplySettings(SettingsModel settings)
        {
            _settings = settings ?? SettingsModel.Defaults();

            if (_mixer == null)
                return;

            _mixer.SetFloat(MasterVolumeParameter, MixerVolume.ToDecibels(_settings.MasterVolume));
            _mixer.SetFloat(MusicVolumeParameter, MixerVolume.ToDecibels(_settings.MusicVolume));
            _mixer.SetFloat(SfxVolumeParameter, MixerVolume.ToDecibels(_settings.SfxVolume));
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _settings = new SettingsStore().Load();

            AudioMixer mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            Configure(mixer, DefaultMaxVoices);
        }

        private void BuildVoices()
        {
            if (_voicesRoot != null)
                Destroy(_voicesRoot.gameObject);

            _voicesRoot = new GameObject("Voices").transform;
            _voicesRoot.SetParent(transform, false);

            _slots = new VoiceSlot[_maxVoices];
            _requests = new VoiceRequest[_maxVoices + 1];

            for (int i = 0; i < _maxVoices; i++)
            {
                var sourceObject = new GameObject($"Voice {i}");
                sourceObject.transform.SetParent(_voicesRoot, false);

                AudioSource source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 2f;
                source.maxDistance = 60f;
                source.dopplerLevel = 0f;
                source.outputAudioMixerGroup = _sfxGroup;

                _slots[i] = new VoiceSlot { Source = source };
            }
        }

        private AudioMixerGroup FindGroup(string groupName)
        {
            if (_mixer == null)
                return null;

            AudioMixerGroup[] groups = _mixer.FindMatchingGroups(groupName);
            return groups != null && groups.Length > 0 ? groups[0] : null;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                VoiceSlot slot = _slots[i];
                if (!slot.Occupied || slot.Source == null || !slot.Source.isPlaying)
                {
                    slot.Occupied = false;
                    return i;
                }
            }

            return -1;
        }

        private void BuildRequests(VoiceRequest incoming)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _requests[i] = _slots[i].Occupied
                    ? _slots[i].Request
                    : new VoiceRequest(float.MaxValue, int.MinValue, 0f);
            }

            _requests[_requests.Length - 1] = incoming;
        }

        private void PlayOn(int index, AudioClip clip, Vector3 position, float volume, VoiceRequest request)
        {
            VoiceSlot slot = _slots[index];
            if (slot.Source == null)
                return;

            slot.Source.transform.position = position;
            slot.Source.clip = clip;
            slot.Source.volume = Mathf.Clamp01(volume);
            slot.Source.Play();

            slot.Request = request;
            slot.Occupied = true;
        }

        private void Stop(int index)
        {
            VoiceSlot slot = _slots[index];
            if (slot.Source != null)
                slot.Source.Stop();

            slot.Occupied = false;
        }

        private float DistanceSquared(Vector3 position)
        {
            if (_listener == null)
                _listener = Object.FindFirstObjectByType<AudioListener>();

            return _listener == null
                ? 0f
                : (_listener.transform.position - position).sqrMagnitude;
        }

        private void OnDestroy()
        {
            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Source != null)
                        _slots[i].Source.Stop();
                }
            }

            if (Instance == this)
                Instance = null;
        }

        private sealed class VoiceSlot
        {
            public AudioSource Source;
            public VoiceRequest Request;
            public bool Occupied;
        }
    }
}
