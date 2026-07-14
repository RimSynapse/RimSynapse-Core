using System;
using UnityEngine;
using Verse;

namespace RimSynapse.Utils
{
    public static class AudioPlaybackManager
    {
        private static GameObject _audioPlayerObject;
        private static AudioSource _audioSource;

        public static bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

        public static void PlayBase64Pcm(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return;
            try
            {
                byte[] audioBytes = Convert.FromBase64String(base64);
                PlayPcm(audioBytes);
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"PlayBase64Pcm failed: {ex.Message}");
            }
        }

        public static void PlayPcm(byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length < 2) return;
            try
            {
                AudioClip clip = LoadPcm(pcmBytes);
                if (clip == null)
                {
                    SynapseLogger.Error("Failed to load PCM audio clip.");
                    return;
                }

                // Ensure execution runs on Unity's main thread since it creates GameObject
                RimSynapse.SynapseGameComponent.Enqueue(() =>
                {
                    try
                    {
                        if (_audioPlayerObject == null)
                        {
                            _audioPlayerObject = new GameObject("RimSynapse_AudioPlayer");
                            UnityEngine.Object.DontDestroyOnLoad(_audioPlayerObject);
                            _audioSource = _audioPlayerObject.AddComponent<AudioSource>();
                        }

                        if (_audioSource.clip != null)
                        {
                            UnityEngine.Object.Destroy(_audioSource.clip);
                        }

                        _audioSource.clip = clip;
                        _audioSource.spatialBlend = 0f;
                        // Double the volume compared to standard UI sounds, clamped to max 1.0f
                        _audioSource.volume = Mathf.Clamp01(Verse.Prefs.VolumeUI * Verse.Prefs.VolumeMaster * 2f);
                        _audioSource.ignoreListenerPause = true;
                        _audioSource.Play();
                    }
                    catch (Exception ex)
                    {
                        SynapseLogger.Error($"Main thread audio playback execution failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"PlayPcm failed: {ex.Message}");
            }
        }

        public static void StopPlayback()
        {
            RimSynapse.SynapseGameComponent.Enqueue(() =>
            {
                if (_audioSource != null && _audioSource.isPlaying)
                {
                    _audioSource.Stop();
                }
            });
        }

        public static AudioClip LoadPcm(byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length < 2) return null;
            int sampleRate = 24000;
            int channels = 1;
            int samplesCount = pcmBytes.Length / 2;

            float[] audioData = new float[samplesCount];
            float boost = RimSynapse.RimSynapseMod.Instance?.Settings?.audioBoost ?? 2.5f;

            for (int i = 0; i < samplesCount; i++)
            {
                short sample = BitConverter.ToInt16(pcmBytes, i * 2);
                float val = (sample / 32768f) * boost;
                audioData[i] = UnityEngine.Mathf.Clamp(val, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("TTS_Audio", samplesCount, channels, sampleRate, false);
            clip.SetData(audioData, 0);
            return clip;
        }
    }
}
