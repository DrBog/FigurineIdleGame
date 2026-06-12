using System.Collections;
using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Identifies the four rotating tabletop biomes. The active biome advances
    /// every three completed waves and drives palette, pacing, and audio mood.
    /// </summary>
    public enum BiomeType
    {
        Forest = 0,
        City = 1,
        Cyber = 2,
        Noir = 3
    }

    /// <summary>
    /// Immutable description of a single biome: its colour palette (environment +
    /// per-archetype enemy tints), its gameplay pacing multipliers, and the audio
    /// mood parameters fed into the <see cref="ProceduralAudioManager"/>.
    /// </summary>
    public class BiomePreset
    {
        public BiomeType Type;
        public string DisplayName;

        // Environment palette.
        public Color PlatformColor;
        public Color BackgroundColor;
        public Color BorderColor;
        public Color AmbientLightColor;      // scene ambient tint for this biome.

        // Per-archetype enemy tints.
        public Color CubeColor;
        public Color SphereColor;
        public Color PyramidColor;

        // The colour the board-wipe vector flash and shatter shards adopt.
        public Color FlashColor;

        // Gameplay pacing multipliers.
        public float SpawnCadenceMultiplier; // >1 = slower spawns, <1 = faster.
        public float EnemySpeedMultiplier;
        public float FireRateMultiplier;     // <1 = faster firing.

        // Procedural audio mood.
        public float AudioRootFreq;
        public int AudioWaveShape;           // 0 = sine, 1 = square.
        public float AudioVolume;
        public float AudioPulseRateHz;
        public float AudioGlitch;            // 0..1 ring-mod / glitch intensity.
    }

    /// <summary>
    /// Owns the biome rotation matrix. Every three waves it performs a kinetic
    /// "DND Board Wipe": it freezes combat, paints a screen-wide vector flash,
    /// shatters every live enemy into a particle cloud, instantly swaps the whole
    /// palette (environment + enemies + audio mood), then releases the freeze.
    ///
    /// Created procedurally by <see cref="GameCore.BuildSubsystems"/>; no scene
    /// authoring or assets are required.
    /// </summary>
    public class BiomeManager : MonoBehaviour
    {
        [Header("Rotation")]
        [Tooltip("Number of waves between biome rotations.")]
        public int wavesPerBiome = 3;

        [Header("Board-Wipe Transition")]
        public float wipeFreezeDuration = 1.15f;
        public float flashFadeInDuration = 0.18f;
        public float flashHoldDuration = 0.12f;
        public float flashFadeOutDuration = 0.55f;

        private GameCore _core;
        private BiomePreset[] _presets;
        private int _activeIndex;
        private bool _wipeInProgress;

        // The full-screen flash image is lazily created on the main canvas.
        private UnityEngine.UI.Image _flashImage;
        private RectTransform _flashRect;

        public BiomeType ActiveBiome => _presets[_activeIndex].Type;
        public BiomePreset ActivePreset => _presets[_activeIndex];

        // ---- Pacing multipliers consumed by the WaveManager -----------------

        public float SpawnCadenceMultiplier => ActivePreset.SpawnCadenceMultiplier;
        public float EnemySpeedMultiplier => ActivePreset.EnemySpeedMultiplier;
        public float FireRateMultiplier => ActivePreset.FireRateMultiplier;

        /// <summary>
        /// Returns the biome tint for a given enemy archetype.
        /// </summary>
        public Color EnemyColorFor(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Cube:
                    return ActivePreset.CubeColor;
                case EnemyArchetype.Sphere:
                    return ActivePreset.SphereColor;
                case EnemyArchetype.Pyramid:
                    return ActivePreset.PyramidColor;
                default:
                    return Color.white;
            }
        }

        public void Initialize(GameCore core)
        {
            _core = core;
            BuildPresets();
            _activeIndex = 0;

            // Apply the opening biome immediately (no transition on boot).
            ApplyBiomeImmediate(_activeIndex);
        }

        /// <summary>
        /// Defines the four biome presets. Colours and pacing are hand-tuned to
        /// match the design brief.
        /// </summary>
        private void BuildPresets()
        {
            _presets = new BiomePreset[4];

            // ---- FOREST: emerald / teal, relaxed pacing, soft audio ----------
            _presets[0] = new BiomePreset
            {
                Type = BiomeType.Forest,
                DisplayName = "VERDANT HOLLOW",
                PlatformColor = new Color(0.10f, 0.24f, 0.18f, 1f),
                BackgroundColor = new Color(0.04f, 0.10f, 0.08f, 1f),
                BorderColor = new Color(0.20f, 0.42f, 0.30f, 1f),
                CubeColor = new Color(0.16f, 0.55f, 0.34f, 1f),
                SphereColor = new Color(0.40f, 0.85f, 0.62f, 1f),
                PyramidColor = new Color(0.62f, 0.92f, 0.55f, 1f),
                FlashColor = new Color(0.55f, 1f, 0.78f, 1f),
                AmbientLightColor = new Color(0.30f, 0.42f, 0.34f, 1f),
                SpawnCadenceMultiplier = 1.25f,
                EnemySpeedMultiplier = 0.9f,
                FireRateMultiplier = 1.2f,
                AudioRootFreq = 110f,   // A2 drone.
                AudioWaveShape = 0,     // sine.
                AudioVolume = 0.42f,
                AudioPulseRateHz = 0.8f,
                AudioGlitch = 0f
            };

            // ---- CITY: amber / gold, aggressive pathing ----------------------
            _presets[1] = new BiomePreset
            {
                Type = BiomeType.City,
                DisplayName = "GILDED SPRAWL",
                PlatformColor = new Color(0.26f, 0.20f, 0.08f, 1f),
                BackgroundColor = new Color(0.12f, 0.09f, 0.03f, 1f),
                BorderColor = new Color(0.52f, 0.40f, 0.16f, 1f),
                CubeColor = new Color(0.85f, 0.62f, 0.18f, 1f),
                SphereColor = new Color(0.96f, 0.78f, 0.32f, 1f),
                PyramidColor = new Color(1f, 0.88f, 0.50f, 1f),
                FlashColor = new Color(1f, 0.84f, 0.42f, 1f),
                AmbientLightColor = new Color(0.46f, 0.38f, 0.22f, 1f),
                SpawnCadenceMultiplier = 0.85f,
                EnemySpeedMultiplier = 1.15f,
                FireRateMultiplier = 0.95f,
                AudioRootFreq = 146.83f, // D3.
                AudioWaveShape = 1,      // square (brassy).
                AudioVolume = 0.46f,
                AudioPulseRateHz = 1.6f,
                AudioGlitch = 0.08f
            };

            // ---- CYBER: cyan / purple, frantic firing, glitch synth ----------
            _presets[2] = new BiomePreset
            {
                Type = BiomeType.Cyber,
                DisplayName = "NEON LATTICE",
                PlatformColor = new Color(0.10f, 0.06f, 0.22f, 1f),
                BackgroundColor = new Color(0.05f, 0.02f, 0.12f, 1f),
                BorderColor = new Color(0.36f, 0.18f, 0.62f, 1f),
                CubeColor = new Color(0.20f, 0.85f, 0.95f, 1f),
                SphereColor = new Color(0.62f, 0.34f, 0.98f, 1f),
                PyramidColor = new Color(0.95f, 0.30f, 0.90f, 1f),
                FlashColor = new Color(0.40f, 0.95f, 1f, 1f),
                AmbientLightColor = new Color(0.30f, 0.22f, 0.52f, 1f),
                SpawnCadenceMultiplier = 0.7f,
                EnemySpeedMultiplier = 1.25f,
                FireRateMultiplier = 0.7f,
                AudioRootFreq = 174.61f, // F3.
                AudioWaveShape = 1,      // square.
                AudioVolume = 0.5f,
                AudioPulseRateHz = 3.2f,
                AudioGlitch = 0.55f
            };

            // ---- NOIR: monochrome grayscale, slow heavy units, deep bass -----
            _presets[3] = new BiomePreset
            {
                Type = BiomeType.Noir,
                DisplayName = "ASHEN VOID",
                PlatformColor = new Color(0.16f, 0.16f, 0.17f, 1f),
                BackgroundColor = new Color(0.02f, 0.02f, 0.02f, 1f),
                BorderColor = new Color(0.42f, 0.42f, 0.44f, 1f),
                CubeColor = new Color(0.30f, 0.30f, 0.32f, 1f),
                SphereColor = new Color(0.62f, 0.62f, 0.64f, 1f),
                PyramidColor = new Color(0.85f, 0.85f, 0.88f, 1f),
                FlashColor = new Color(0.92f, 0.92f, 0.95f, 1f),
                AmbientLightColor = new Color(0.34f, 0.34f, 0.36f, 1f),
                SpawnCadenceMultiplier = 1.1f,
                EnemySpeedMultiplier = 0.78f,
                FireRateMultiplier = 1.0f,
                AudioRootFreq = 73.42f,  // D2 deep bass.
                AudioWaveShape = 0,      // sine.
                AudioVolume = 0.5f,
                AudioPulseRateHz = 0.55f,
                AudioGlitch = 0.0f
            };
        }

        /// <summary>
        /// Called by the WaveManager at the start of every wave. Triggers a
        /// board-wipe + biome advance whenever the wave count crosses a multiple
        /// of <see cref="wavesPerBiome"/>.
        /// </summary>
        public void OnWaveStarted(int waveNumber)
        {
            if (_core == null || waveNumber <= 1)
            {
                return;
            }

            // Rotate at wave 4, 7, 10, ... (i.e. every wavesPerBiome after the
            // opening block). (waveNumber - 1) % wavesPerBiome == 0 fires the
            // transition exactly once per boundary.
            if ((waveNumber - 1) % Mathf.Max(1, wavesPerBiome) == 0 && !_wipeInProgress)
            {
                int nextIndex = (_activeIndex + 1) % _presets.Length;
                StartCoroutine(BoardWipeRoutine(nextIndex));
            }
        }

        /// <summary>
        /// The kinetic DND board-wipe. Freezes combat, flashes the screen, shatters
        /// every enemy, swaps the palette + audio mood, then releases the freeze.
        /// </summary>
        private IEnumerator BoardWipeRoutine(int nextIndex)
        {
            _wipeInProgress = true;

            bool wasFrozen = _core.CombatFrozen;
            _core.SetCombatFrozen(true);

            EnsureFlashImage();
            BiomePreset incoming = _presets[nextIndex];
            Color flash = incoming.FlashColor;

            // --- Phase 1: vector flash fades IN to full opacity ---------------
            yield return RunFlash(flash, 0f, 0.92f, flashFadeInDuration);

            // --- Phase 2: at peak whiteout, mutate the world ------------------
            // Shatter every live enemy into a particle cloud using the flash hue.
            if (_core.WaveManager != null)
            {
                _core.WaveManager.ShatterAllEnemies(flash);
            }

            // Commit the new biome index and repaint everything.
            _activeIndex = nextIndex;
            ApplyBiomeImmediate(_activeIndex);

            // Hold the whiteout briefly so the swap reads as instant.
            yield return new WaitForSecondsRealtime(flashHoldDuration);

            // --- Phase 3: flash fades OUT, revealing the new biome ------------
            yield return RunFlash(flash, 0.92f, 0f, flashFadeOutDuration);

            // Restore the prior freeze state (don't unfreeze if we were paused /
            // in menu for some reason).
            _core.SetCombatFrozen(wasFrozen);
            _wipeInProgress = false;
        }

        /// <summary>
        /// Lerps the full-screen flash image alpha from <paramref name="fromAlpha"/>
        /// to <paramref name="toAlpha"/> over <paramref name="duration"/> seconds
        /// (using unscaled time so it animates even while combat is frozen).
        /// </summary>
        private IEnumerator RunFlash(Color hue, float fromAlpha, float toAlpha, float duration)
        {
            if (_flashImage == null)
            {
                yield break;
            }

            _flashImage.gameObject.SetActive(true);

            float t = 0f;
            duration = Mathf.Max(0.0001f, duration);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float a = Mathf.Lerp(fromAlpha, toAlpha, k);
                _flashImage.color = new Color(hue.r, hue.g, hue.b, a);
                yield return null;
            }

            _flashImage.color = new Color(hue.r, hue.g, hue.b, toAlpha);
            if (toAlpha <= 0.001f)
            {
                _flashImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Instantly applies a biome's palette + audio mood with no animation.
        /// Used on boot and at the peak of a board-wipe.
        /// </summary>
        private void ApplyBiomeImmediate(int index)
        {
            BiomePreset p = _presets[index];

            // Environment palette (platform, borders, camera background).
            if (_core != null)
            {
                _core.ApplyEnvironmentPalette(p.PlatformColor, p.BackgroundColor, p.BorderColor);
            }

            // Repaint every live enemy to the new archetype tints.
            if (_core != null && _core.WaveManager != null)
            {
                _core.WaveManager.RepaintEnemies();
            }

            // Shift the generative audio mood.
            if (_core != null && _core.Audio != null)
            {
                _core.Audio.SetBiome(
                    p.AudioRootFreq,
                    p.AudioWaveShape,
                    p.AudioVolume,
                    p.AudioPulseRateHz,
                    p.AudioGlitch);
            }
        }

        // ============================================================
        //  Phase 3 board-wipe API (Time.timeScale + camera flash quad +
        //  tag-based enemy destruction). Used by AdvancedWaveSpawner.
        // ============================================================

        /// <summary>
        /// Called by the wave spawner at the start of each wave. Triggers a biome
        /// rotation + board wipe every <see cref="wavesPerBiome"/> waves.
        /// </summary>
        public void CheckForBiomeTransition(int waveNumber)
        {
            if (_core == null || waveNumber <= 1)
            {
                return;
            }

            if ((waveNumber - 1) % Mathf.Max(1, wavesPerBiome) == 0 && !_wipeInProgress)
            {
                ExecuteBoardWipe();
            }
        }

        /// <summary>
        /// Kicks off the kinetic DND board wipe (slow-mo freeze, camera flash,
        /// tag-based enemy shatter, palette + audio shift). Safe to call directly.
        /// </summary>
        public void ExecuteBoardWipe()
        {
            if (!_wipeInProgress && _presets != null)
            {
                StartCoroutine(BoardWipeTimeScaleRoutine());
            }
        }

        private IEnumerator BoardWipeTimeScaleRoutine()
        {
            _wipeInProgress = true;

            // 1. Freeze: drop into slow-motion and freeze the combat layer.
            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0.1f;
            if (_core != null)
            {
                _core.SetCombatFrozen(true);
            }

            int nextIndex = (_activeIndex + 1) % _presets.Length;
            Color flash = _presets[nextIndex].FlashColor;

            // 2. Camera flash: instantiate a white quad overlay in front of the camera.
            GameObject flashQuad = CreateCameraFlashQuad(flash);

            // 3. Destroy every tagged enemy, spawning a particle burst at each.
            DestroyAllTaggedEnemies(flash);

            // 4. Hold the whiteout briefly (real time), fading the quad out.
            float hold = 0.4f;
            float t = 0f;
            while (t < hold)
            {
                t += Time.unscaledDeltaTime;
                if (flashQuad != null)
                {
                    var r = flashQuad.GetComponent<Renderer>();
                    if (r != null)
                    {
                        Color c = r.material.color;
                        c.a = Mathf.Lerp(0.92f, 0f, t / hold);
                        SetRendererColor(r, c);
                    }
                }
                yield return null;
            }

            // 5. Switch to the next biome and apply all visuals + audio.
            _activeIndex = nextIndex;
            ApplyBiomeVisuals();

            if (flashQuad != null)
            {
                Destroy(flashQuad);
            }

            // 6. Restore time scale and release the combat freeze.
            Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
            if (_core != null)
            {
                _core.SetCombatFrozen(false);
            }
            _wipeInProgress = false;
        }

        /// <summary>
        /// Applies the active biome's full visual + audio identity: ambient light,
        /// environment palette, enemy repaint, and the generative drone mood.
        /// </summary>
        public void ApplyBiomeVisuals()
        {
            if (_presets == null)
            {
                return;
            }

            BiomePreset p = _presets[_activeIndex];

            // Ambient scene light.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = p.AmbientLightColor;

            // Environment palette (platform material, borders, camera background).
            if (_core != null)
            {
                _core.ApplyEnvironmentPalette(p.PlatformColor, p.BackgroundColor, p.BorderColor);

                if (_core.WaveManager != null)
                {
                    _core.WaveManager.RepaintEnemies();
                }

                if (_core.Audio != null)
                {
                    _core.Audio.SetBiome(p.AudioRootFreq, p.AudioWaveShape, p.AudioVolume, p.AudioPulseRateHz, p.AudioGlitch);
                    _core.Audio.SetDroneFrequency(p.AudioRootFreq);
                }
            }
        }

        /// <summary>
        /// Creates a large transparent quad parented to the main camera so it fills
        /// the view as a kinetic flash overlay.
        /// </summary>
        private GameObject CreateCameraFlashQuad(Color color)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return null;
            }

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BiomeBoardWipeQuad";
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            quad.transform.SetParent(cam.transform, false);
            float dist = cam.nearClipPlane + 0.25f;
            quad.transform.localPosition = new Vector3(0f, 0f, dist);
            quad.transform.localRotation = Quaternion.identity;

            float h = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * Mathf.Max(0.1f, cam.aspect);
            quad.transform.localScale = new Vector3(w * 1.3f, h * 1.3f, 1f);

            var rend = quad.GetComponent<Renderer>();
            Color start = new Color(color.r, color.g, color.b, 0.92f);
            rend.material = MakeTransparentMaterial(start);
            SetRendererColor(rend, start);

            return quad;
        }

        /// <summary>
        /// Finds all GameObjects tagged "Enemy", spawns a particle burst at each,
        /// and destroys them. Also shatters the legacy wave manager's enemy list.
        /// </summary>
        private void DestroyAllTaggedEnemies(Color shardColor)
        {
            GameObject[] tagged;
            try
            {
                tagged = GameObject.FindGameObjectsWithTag("Enemy");
            }
            catch (UnityException)
            {
                // The "Enemy" tag may not be defined in the project's TagManager.
                tagged = new GameObject[0];
            }

            for (int i = tagged.Length - 1; i >= 0; i--)
            {
                if (tagged[i] == null)
                {
                    continue;
                }
                SpawnParticleBurst(tagged[i].transform.position, shardColor);
                Destroy(tagged[i]);
            }

            // The procedural WaveManagerAlpha tracks its own enemy list separately.
            if (_core != null && _core.WaveManager != null)
            {
                _core.WaveManager.ShatterAllEnemies(shardColor);
            }
        }

        /// <summary>
        /// Spawns a short-lived procedural particle burst at a world position.
        /// </summary>
        private void SpawnParticleBurst(Vector3 position, Color color)
        {
            var go = new GameObject("BoardWipeBurst");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startSpeed = 6f;
            main.startSize = 0.4f;
            main.startLifetime = 0.6f;
            main.startColor = color;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            var psRenderer = go.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                psRenderer.material = MakeTransparentMaterial(color);
            }

            ps.Play();
            Destroy(go, 1.2f);
        }

        /// <summary>
        /// Builds a material that renders with alpha transparency across the common
        /// render pipelines, so the flash quad and particle bursts fade correctly.
        /// </summary>
        private static Material MakeTransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            // Configure the Standard shader for alpha blending where applicable.
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3f); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // URP transparent surface type
            }
            mat.renderQueue = 3000;
            return mat;
        }

        /// <summary>Sets a renderer's colour across pipeline colour properties.</summary>
        private static void SetRendererColor(Renderer rend, Color color)
        {
            if (rend == null)
            {
                return;
            }
            rend.material.color = color;
            if (rend.material.HasProperty("_BaseColor"))
            {
                rend.material.SetColor("_BaseColor", color);
            }
        }

        /// <summary>
        /// Lazily builds the screen-wide flash <see cref="UnityEngine.UI.Image"/>
        /// that covers the entire main canvas.
        /// </summary>
        private void EnsureFlashImage()
        {
            if (_flashImage != null || _core == null || _core.MainCanvas == null)
            {
                return;
            }

            var go = new GameObject("BiomeBoardWipeFlash");
            go.transform.SetParent(_core.MainCanvas.transform, false);

            _flashImage = go.AddComponent<UnityEngine.UI.Image>();
            _flashImage.raycastTarget = false;
            _flashImage.color = new Color(1f, 1f, 1f, 0f);

            _flashRect = _flashImage.rectTransform;
            _flashRect.anchorMin = Vector2.zero;
            _flashRect.anchorMax = Vector2.one;
            _flashRect.offsetMin = Vector2.zero;
            _flashRect.offsetMax = Vector2.zero;

            // Draw above everything else on the canvas.
            go.transform.SetAsLastSibling();
            go.SetActive(false);
        }
    }
}
