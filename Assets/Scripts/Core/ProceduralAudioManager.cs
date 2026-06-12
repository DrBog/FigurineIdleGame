using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Procedural audio synthesis engine. Generates ALL game audio from raw math
    /// using <see cref="OnAudioFilterRead"/> — no audio assets are used. It mixes:
    ///   * a rhythmic generative drone baseline whose pitch/shape follows the biome,
    ///   * triggered laser firing tones (pitch-swept square/sine blend), and
    ///   * triggered impact bursts (decaying filtered noise).
    ///
    /// OnAudioFilterRead runs on the audio thread, so the public trigger methods only
    /// set plain numeric/bool fields that the DSP loop consumes. We keep a looping
    /// silent AudioSource alive so Unity keeps calling the filter callback.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralAudioManager : MonoBehaviour
    {
        private GameCore _core;
        private AudioSource _source;

        private double _sampleRate = 48000.0;

        [Header("Master")]
        [Range(0f, 1f)] public float masterVolume = 0.5f;

        // ----- Drone (biome baseline) -----
        // These are written by the main thread (SetBiome) and read on the audio thread.
        private float _droneFreq = 55f;        // root frequency (Hz)
        private float _droneTargetFreq = 55f;
        private float _droneVolume = 0.22f;
        private int _droneWaveShape = 0;       // 0 = sine, 1 = square
        private float _droneGlitch = 0f;       // 0..1 modulation amount (cyber biome)
        private double _dronePhase;
        private double _dronePhase2;           // detuned layer for a thicker pad
        private double _lfoPhase;              // rhythmic pulse LFO
        private float _pulseRateHz = 1.6f;     // drone rhythm (beats/sec)

        // ----- Laser voice (triggered) -----
        private volatile bool _laserTrigger;
        private float _laserEnv;               // 0..1 envelope
        private double _laserPhase;
        private float _laserFreq;
        private float _laserFreqStart = 1400f;
        private float _laserFreqEnd = 320f;
        private float _laserDecay = 7.5f;      // env units/sec

        // ----- Impact voice (triggered) -----
        private volatile bool _impactTrigger;
        private float _impactEnv;
        private float _impactDecay = 9.0f;
        private double _impactPhase;
        private float _impactTone = 90f;       // low body tone for the burst
        private uint _noiseState = 0x1234567u;  // xorshift RNG state (audio-thread safe)

        public void Initialize(GameCore core)
        {
            _core = core;

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

        #region Public trigger / control API (main thread)

        /// <summary>Fires a procedural laser tone (player / pyramid shots).</summary>
        public void PlayLaser()
        {
            _laserTrigger = true;
        }

        /// <summary>Fires a procedural impact burst (hits, kills, dashes).</summary>
        public void PlayImpact()
        {
            _impactTrigger = true;
        }

        /// <summary>
        /// Sets the generative drone baseline parameters for the active biome.
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

        private void OnAudioFilterRead(float[] data, int channels)
        {
            double sr = _sampleRate;
            double dronePhaseInc = _droneFreq / sr;
            double lfoInc = _pulseRateHz / sr;

            for (int n = 0; n < data.Length; n += channels)
            {
                // Smoothly glide the drone toward the biome target frequency.
                _droneFreq += (_droneTargetFreq - _droneFreq) * 0.0008f;
                dronePhaseInc = _droneFreq / sr;

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

                // --- Laser voice ---
                if (_laserTrigger)
                {
                    _laserTrigger = false;
                    _laserEnv = 1f;
                    _laserFreq = _laserFreqStart;
                    _laserPhase = 0.0;
                }
                if (_laserEnv > 0f)
                {
                    // Pitch sweep downwards over the envelope.
                    _laserFreq += (_laserFreqEnd - _laserFreq) * 0.002f;
                    _laserPhase += _laserFreq / sr;
                    if (_laserPhase > 1.0) _laserPhase -= 1.0;

                    float sine = Mathf.Sin((float)(_laserPhase * 2.0 * Mathf.PI));
                    float square = _laserPhase < 0.5 ? 1f : -1f;
                    float laser = Mathf.Lerp(sine, square, 0.5f) * _laserEnv * 0.35f;
                    sample += laser;

                    _laserEnv -= _laserDecay / (float)sr;
                    if (_laserEnv < 0f) _laserEnv = 0f;
                }

                // --- Impact voice ---
                if (_impactTrigger)
                {
                    _impactTrigger = false;
                    _impactEnv = 1f;
                    _impactPhase = 0.0;
                }
                if (_impactEnv > 0f)
                {
                    _impactPhase += _impactTone / sr;
                    if (_impactPhase > 1.0) _impactPhase -= 1.0;
                    float body = Mathf.Sin((float)(_impactPhase * 2.0 * Mathf.PI));
                    float noise = NextNoise();
                    // Body-heavy at the start, noisy crack on the attack.
                    float impact = (body * 0.6f + noise * 0.4f) * _impactEnv * _impactEnv * 0.5f;
                    sample += impact;

                    _impactEnv -= _impactDecay / (float)sr;
                    if (_impactEnv < 0f) _impactEnv = 0f;
                }

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
