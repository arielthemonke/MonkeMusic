using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine.Networking;

namespace MonkeMusic
{
	[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private AudioSource audioSource;
        private string musicFolderPath;
        private AudioClip[] loadedClips;
        private int currentClipIndex = 0;
        private bool isPlaying = false;
        private bool isPaused = false;
        private float lastPrimaryPressTime;
        private float lastSecondaryPressTime;
        private float lastStopPressTime;
        private ConfigEntry<float> configCooldown;


        // Config file stuff
        private ConfigEntry<float> configVolume;
        private ConfigEntry<KeyboardShortcut> configPlayPause;
        private ConfigEntry<KeyboardShortcut> configStop;
        private ConfigEntry<KeyboardShortcut> configNext;
        private ConfigEntry<KeyboardShortcut> configVolUp;
        private ConfigEntry<KeyboardShortcut> configVolDown;

        void Awake()
        {
            configVolume = Config.Bind("Settings", "Volume", 0.5f, "Current volume level");
            configPlayPause = Config.Bind("Controls", "Play/Pause", new KeyboardShortcut(KeyCode.P));
            configStop = Config.Bind("Controls", "Stop", new KeyboardShortcut(KeyCode.S));
            configNext = Config.Bind("Controls", "Next", new KeyboardShortcut(KeyCode.N));
            configVolUp = Config.Bind("Controls", "Volume Up", new KeyboardShortcut(KeyCode.UpArrow));
            configVolDown = Config.Bind("Controls", "Volume Down", new KeyboardShortcut(KeyCode.DownArrow));
            configCooldown = Config.Bind("Settings", "ControllerCooldown", 0.5f, "Cooldown between controller inputs (seconds)");

            musicFolderPath = Path.Combine(Path.GetDirectoryName(Info.Location), "Music");
            Directory.CreateDirectory(musicFolderPath);
            StartCoroutine(InitializeMusicSystem());
        }

        

        IEnumerator InitializeMusicSystem()
        {
            yield return StartCoroutine(LoadAudioClips());

            if (loadedClips.Length > 0)
            {
                InitializeAudioSource();
                audioSource.volume = configVolume.Value;
                StartPlayback();
            }
        }

        void InitializeAudioSource()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0;
            audioSource.loop = false;
        }

        void Update()
        {
            HandleInput();
        }

        void HandleInput()
        {
            float currentTime = Time.time;
            if ((configPlayPause.Value.IsDown() || (ControllerInputPoller.instance.rightControllerPrimaryButton && currentTime - lastPrimaryPressTime > configCooldown.Value)) && !ControllerInputPoller.instance.leftControllerPrimaryButton)
            {
                if (ControllerInputPoller.instance.rightControllerPrimaryButton)
                    lastPrimaryPressTime = currentTime;

                if (isPlaying)
                {
                    if (isPaused) ResumePlayback();
                    else PausePlayback();
                }
                else if (loadedClips.Length > 0)
                {
                    StartPlayback();
                }
            }
            if ((configStop.Value.IsDown() || (ControllerInputPoller.instance.leftControllerPrimaryButton && currentTime - lastStopPressTime > configCooldown.Value)))
            {
                if (ControllerInputPoller.instance.leftControllerPrimaryButton)
                    lastStopPressTime = currentTime;
                StopPlayback();
            }
            if (((configNext.Value.IsDown() || ControllerInputPoller.instance.rightControllerSecondaryButton && currentTime - lastSecondaryPressTime > configCooldown.Value)) && isPlaying)
            {
                if (ControllerInputPoller.instance.rightControllerSecondaryButton)
                    lastSecondaryPressTime = currentTime;
                SkipToNextSong();
            }
            if (configVolUp.Value.IsDown())
            {
                SetVolume(configVolume.Value + 0.1f);
            }
            if (configVolDown.Value.IsDown())
            {
                SetVolume(configVolume.Value - 0.1f);
            }
        }
        void SetVolume(float newVolume)
        {
            configVolume.Value = Mathf.Clamp01(newVolume);
            audioSource.volume = configVolume.Value;
            Config.Save();
        }
        void StartPlayback()
        {
            if (loadedClips.Length == 0) return;

            isPlaying = true;
            isPaused = false;

            if (audioSource.isPlaying) audioSource.Stop();

            PlayCurrentSong();
        }
        void PlayCurrentSong()
        {
            audioSource.clip = loadedClips[currentClipIndex];
            audioSource.Play();
            Invoke(nameof(PlayNextSong), audioSource.clip.length);
        }

        void PlayNextSong()
        {
            if (!isPlaying) return;

            currentClipIndex = (currentClipIndex + 1) % loadedClips.Length;
            PlayCurrentSong();
        }

        void PausePlayback()
        {
            isPaused = true;
            audioSource.Pause();
            CancelInvoke(nameof(PlayNextSong));
        }

        void ResumePlayback()
        {
            isPaused = false;
            audioSource.UnPause();
            float remainingTime = audioSource.clip.length - audioSource.time;
            Invoke(nameof(PlayNextSong), remainingTime);
        }

        void StopPlayback()
        {
            isPlaying = false;
            isPaused = false;
            audioSource.Stop();
            CancelInvoke(nameof(PlayNextSong));
            currentClipIndex = 0;
        }

        void SkipToNextSong()
        {
            if (!isPlaying) return;

            CancelInvoke(nameof(PlayNextSong));
            currentClipIndex = (currentClipIndex + 1) % loadedClips.Length;
            PlayCurrentSong();
        }

        IEnumerator LoadAudioClips()
        {
            var validExtensions = new[] { ".wav", ".ogg", ".mp3" };
            var audioFiles = Directory.GetFiles(musicFolderPath)
                .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToArray();

            loadedClips = new AudioClip[audioFiles.Length];

            for (int i = 0; i < audioFiles.Length; i++)
            {
                yield return StartCoroutine(LoadAudioFile(audioFiles[i], i));
            }
        }

        IEnumerator LoadAudioFile(string path, int index)
        {
            string ext = Path.GetExtension(path).ToLower();
            AudioType audioType = AudioType.UNKNOWN;

            switch (ext)
            {
                case ".wav":
                    audioType = AudioType.WAV;
                    break;
                case ".ogg":
                    audioType = AudioType.OGGVORBIS;
                    break;
                case ".mp3":
                    audioType = AudioType.MPEG;
                    break;
            }

            using (var www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    loadedClips[index] = DownloadHandlerAudioClip.GetContent(www);
                    loadedClips[index].name = Path.GetFileNameWithoutExtension(path);
                }
                else
                {
                    Debug.LogError($"Failed to load {path}: {www.error}");
                }
            }
        }
    }
}
