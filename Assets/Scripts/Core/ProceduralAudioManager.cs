using System.Collections.Generic;
using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Procedural audio synthesis engine. Generates ALL game audio from raw math
    /// inside <see cref="OnAudioFilterRead"/> — no audio assets are used. It mixes:
    ///   * a rhythmic generative drone baseline whose pitch/shape follows the biome,
    ///   * and a pool of polyphonic one-shot voices (laser sweeps, explosion bursts,
    ///     impact pops and UI ticks) so many effects can sound simultaneously.
    ///
    /// OnAudioFilterRead runs on the audio thread, so the public trigger methods only
    /// enqueue lightweight requests that the DSP loop drains once per audio buffer.
    /// A looping silent AudioSource keeps Unity calling the filter callback.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralAudioManager : MonoBehaviour
    {
        /// <summary>Global access point so any system can request a sound.</summary>
        public static ProceduralAudioManager Instance { get; private set; }

        private GameCore _core;
        private AudioSource _source;

        private double _sampleRate = 48000.0;

        [Header("Master")]
        [Range(0f, 1f)] public float masterVolume = 0.5f;
        [Range(0f, 1f)] public float sfxVolume = 0.85f;

        // ----- Drone (biome baseline) -----------------------------------------
        // Written by the main thread (SetBiome / SetDroneFrequency), read on the
        // audio thread. Plain fields — no references shared across threads.
        private float _droneFreq = 55f;        // current root frequency (Hz)
        private float _droneTargetFreq = 55f;  // glide target
        private float _droneVolume = 0.22f;
        private int _droneWaveShape = 0;       // 0 = sine, 1 = square
        private float _droneGlitch = 0f;       // 0..1 ring-mod amount (cyber biome)
        private double _dronePhase;
        private double _dronePhase2;           // detuned layer for a thicker pad
        private double _lfoPhase;              // rhythmic pulse LFO
        private float _pulseRateHz = 1.6f;     // drone rhythm (beats/sec)

        // ----- One-shot voice pool (polyphonic) -------------------------------
        private enum SfxType { Laser, Explosion, Impact, UITick }

        private struct Voice
        {
            public bool active;
            public SfxType type;
            public double phase;
            public float freq;       // current frequency (Hz)
            public float freqStart;  // sweep start (env == 1)
            public float freqEnd;    // sweep end   (env == 0)
            public float env;        // 1 -> 0 amplitude envelope
            public float decayPerSec;// envelope units consumed per second
            public float gain;       // peak mix gain
        }

        private const int VoiceCount = 24;
        private Voice[] _voices;

        // Thread-safe trigger queue (main thread enqueues, audio thread drains).
        private readonly Queue<SfxType> _pending = new Queue<SfxType>(32);
        private readonly object _queueLock = new object();

        // xorshift32 RNG state for allocation-free white noise on the audio thread.
        private uint _noiseState = 0x1234567u;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _voices = new Voice[VoiceCount];
        }

        public void Initialize(GameCore core)
        {
            _core = core;
            Instance = this;

            if (_voices == null)
            {
                _voices = new Voice[VoiceCount];
            }

            _source = GetComponent<AudioSource>();
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }

            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000.0;

            // A looping (silent) clip keeps the DSP/filter callback running. The
            // actual sound is synthesized entirely inside OnAudioFilterRead.
            int clipLen = (int)_sampleRate; // 1 second
            var clip = AudioClip.Create("ProceduralSilence", clipLen, 1, (int)_sampleRate, false);
            _source.clip = clip;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // 2D
            _source.volume = 1f;
            _source.Play();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #region Public trigger / control API (main thread)

        /// <summary>
        /// Sharp laser tone: frequency sweep from 800Hz down to 200Hz over ~0.15s.
        /// </summary>
        public void PlayLaserTone()
        {
            Enqueue(SfxType.Laser);
        }

        /// <summary>
        /// White-noise explosion burst with a fast-decaying envelope (~0.2s).
        /// </summary>
        public void PlayExplosionBurst()
        {
            Enqueue(SfxType.Explosion);
        }

        /// <summary>
        /// Short impact click/pop: pitch drop 150Hz -> 50Hz over ~0.08s.
        /// </summary>
        public void PlayImpactSound()
        {
            Enqueue(SfxType.Impact);
        }

        /// <summary>
        /// High-pitched UI blip (1200Hz, ~0.05s) for menu / interface feedback.
        /// </summary>
        public void PlayUITick()
        {
            Enqueue(SfxType.UITick);
        }

        // ----- Backwards-compatible aliases used by other Phase 2 systems -----

        /// <summary>Alias for <see cref="PlayLaserTone"/> (player / pyramid shots).</summary>
        public void PlayLaser()
        {
            Enqueue(SfxType.Laser);
        }

        /// <summary>Alias for <see cref="PlayImpactSound"/> (hits, kills, dashes).</summary>
        public void PlayImpact()
        {
            Enqueue(SfxType.Impact);
        }

        /// <summary>
        /// Sets only the generative drone root frequency. Called by the BiomeManager
        /// when a biome shift should retune the background baseline.
        /// </summary>
        public void SetDroneFrequency(float freq)
        {
            _droneTargetFreq = Mathf.Max(20f, freq);
        }

        /// <summary>
        /// Sets the full generative drone baseline parameters for the active biome.
        /// </summary>
        /// <param name="rootFreq">Drone root frequency in Hz.</param>
        /// <param name="waveShape">0 = sine, 1 = square.</param>
        /// <param name="volume">Drone mix volume (0..1).</param>
        /// <param name="pulseRateHz">Rhythmic pulse rate in beats/second.</param>
        /// <param name="glitch">Glitch modulation amount (0..1).</param>
        public void SetBiome(float rootFreq, int waveShape, float volume, float pulseRateHz, float glitch)
        {
            _droneTargetFreq = Mathf.Max(20f, rootFreq);
            _droneWaveShape = waveShape;
            _droneVolume = Mathf.Clamp01(volume);
            _pulseRateHz = Mathf.Max(0.1f, pulseRateHz);
            _droneGlitch = Mathf.Clamp01(glitch);
        }

        private void Enqueue(SfxType type)
        {
            lock (_queueLock)
            {
                // Bound the queue so a runaway caller can never grow it unbounded.
                if (_pending.Count < 64)
                {
                    _pending.Enqueue(type);
                }
            }
        }

        #endregion

        #region DSP (audio thread)

        private float NextNoise()
        {
            // xorshift32 — deterministic, allocation-free white noise in [-1, 1].
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 17;
            _noiseState ^= _noiseState << 5;
            return (_noiseState / (float)uint.MaxValue) * 2f - 1f;
        }

        /// <summary>
        /// Pulls every queued request and assigns it to a free voice slot. Runs once
        /// per audio buffer so the lock is contended at most a few hundred times/sec.
        /// </summary>
        private void DrainPendingVoices()
        {
            lock (_queueLock)
            {
                while (_pending.Count > 0)
                {
                    SfxType type = _pending.Dequeue();
                    AssignVoice(type);
                }
            }
        }

        private void AssignVoice(SfxType type)
        {
            int slot = -1;
            float lowestEnv = float.MaxValue;
            int lowestIdx = 0;

            for (int i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].active)
                {
                    slot = i;
                    break;
                }
                if (_voices[i].env < lowestEnv)
                {
                    lowestEnv = _voices[i].env;
                    lowestIdx = i;
                }
            }

            // All voices busy: steal the quietest one (voice stealing).
            if (slot < 0)
            {
                slot = lowestIdx;
            }

            Voice v = new Voice { active = true, type = type, phase = 0.0, env = 1f };

            switch (type)
            {
                case SfxType.Laser:
                    v.freqStart = 800f;
                    v.freqEnd = 200f;
                    v.freq = v.freqStart;
                    v.decayPerSec = 1f / 0.15f;
                    v.gain = 0.33f;
                    break;
                case SfxType.Explosion:
                    v.freqStart = 0f;
                    v.freqEnd = 0f;
                    v.freq = 0f;
                    v.decayPerSec = 1f / 0.20f;
                    v.gain = 0.50f;
                    break;
                case SfxType.Impact:
                    v.freqStart = 150f;
                    v.freqEnd = 50f;
                    v.freq = v.freqStart;
                    v.decayPerSec = 1f / 0.08f;
                    v.gain = 0.50f;
                    break;
                case SfxType.UITick:
                    v.freqStart = 1200f;
                    v.freqEnd = 1200f;
                    v.freq = 1200f;
                    v.decayPerSec = 1f / 0.05f;
                    v.gain = 0.25f;
                    break;
            }

            _voices[slot] = v;
        }

        private float RenderVoice(ref Voice v, double sr)
        {
            if (!v.active)
            {
                return 0f;
            }

            // Linear pitch sweep keyed to the envelope (env 1 -> start, 0 -> end).
            v.freq = Mathf.Lerp(v.freqEnd, v.freqStart, v.env);

            float outSample;
            switch (v.type)
            {
                case SfxType.Explosion:
                {
                    // Decaying white noise; env squared gives a punchier transient.
                    float noise = NextNoise();
                    outSample = noise * v.env * v.env * v.gain;
                    break;
                }
                case SfxType.Impact:
                {
                    v.phase += v.freq / sr;
                    if (v.phase > 1.0) v.phase -= 1.0;
                    float body = Mathf.Sin((float)(v.phase * 2.0 * Mathf.PI));
                    float noise = NextNoise();
                    outSample = (body * 0.65f + noise * 0.35f) * v.env * v.env * v.gain;
                    break;
                }
                case SfxType.UITick:
                {
                    v.phase += v.freq / sr;
                    if (v.phase > 1.0) v.phase -= 1.0;
                    float sine = Mathf.Sin((float)(v.phase * 2.0 * Mathf.PI));
                    outSample = sine * v.env * v.gain;
                    break;
                }
                default: // Laser
                {
                    v.phase += v.freq / sr;
                    if (v.phase > 1.0) v.phase -= 1.0;
                    float sine = Mathf.Sin((float)(v.phase * 2.0 * Mathf.PI));
                    float square = v.phase < 0.5 ? 1f : -1f;
                    outSample = Mathf.Lerp(sine, square, 0.5f) * v.env * v.gain;
                    break;
                }
            }

            v.env -= v.decayPerSec / (float)sr;
            if (v.env <= 0f)
            {
                v.env = 0f;
                v.active = false;
            }

            return outSample;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_voices == null)
            {
                return;
            }

            DrainPendingVoices();

            double sr = _sampleRate;
            double lfoInc = _pulseRateHz / sr;

            for (int n = 0; n < data.Length; n += channels)
            {
                // Smoothly glide the drone toward the biome target frequency.
                _droneFreq += (_droneTargetFreq - _droneFreq) * 0.0008f;
                double dronePhaseInc = _droneFreq / sr;

                // --- Drone baseline ---
                _dronePhase += dronePhaseInc;
                if (_dronePhase > 1.0) _dronePhase -= 1.0;
                _dronePhase2 += dronePhaseInc * 1.005; // slight detune
                if (_dronePhase2 > 1.0) _dronePhase2 -= 1.0;

                float droneOsc;
                if (_droneWaveShape == 1)
                {
                    droneOsc = (_dronePhase < 0.5 ? 1f : -1f) * 0.6f
                             + Mathf.Sin((float)(_dronePhase2 * 2.0 * Mathf.PI)) * 0.4f;
                }
                else
                {
                    droneOsc = Mathf.Sin((float)(_dronePhase * 2.0 * Mathf.PI)) * 0.6f
                             + Mathf.Sin((float)(_dronePhase2 * 2.0 * Mathf.PI)) * 0.4f;
                }

                // Rhythmic amplitude pulse (generative beat).
                _lfoPhase += lfoInc;
                if (_lfoPhase > 1.0) _lfoPhase -= 1.0;
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((float)(_lfoPhase * Mathf.PI)));

                // Optional glitch modulation (ring-mod style) for the cyber biome.
                if (_droneGlitch > 0.001f)
                {
                    float ring = Mathf.Sin((float)(_dronePhase * 2.0 * Mathf.PI) * 7.13f);
                    droneOsc = Mathf.Lerp(droneOsc, droneOsc * ring, _droneGlitch);
                }

                float sample = droneOsc * _droneVolume * pulse;

                // --- One-shot voices (polyphonic mix) ---
                float sfx = 0f;
                for (int i = 0; i < _voices.Length; i++)
                {
                    if (_voices[i].active)
                    {
                        sfx += RenderVoice(ref _voices[i], sr);
                    }
                }
                sample += sfx * sfxVolume;

                // Master + soft clip.
                sample *= masterVolume;
                if (sample > 1f) sample = 1f;
                else if (sample < -1f) sample = -1f;

                for (int c = 0; c < channels; c++)
                {
                    data[n + c] = sample;
                }
            }
        }

        #endregion
    }
}
