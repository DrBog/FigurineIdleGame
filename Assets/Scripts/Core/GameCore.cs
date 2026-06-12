using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Master state machine and initialization hub for the FigurineIdleGame.
    /// Bootstraps the entire game at runtime using only Unity primitives.
    /// Owns the tabletop diorama platform, the player, the responsive UI canvas,
    /// and the gameplay subsystems (menu, waves, health/score, input).
    /// </summary>
    public class GameCore : MonoBehaviour
    {
        public enum GameState
        {
            MainMenu,
            ActiveWave,
            Paused
        }

        public static GameCore Instance { get; private set; }

        [Header("Balance Parameters")]
        public float playerMaxHealth = 100.0f;
        public float playerMoveSpeed = 7.5f;
        public float playerDamage = 25.0f;
        public int startingCurrency = 0;

        [Header("Runtime State")]
        public GameState State { get; private set; } = GameState.MainMenu;
        public int Currency { get; private set; }

        // True while combat logic (movement, spawning, firing) must remain frozen.
        public bool CombatFrozen { get; private set; } = true;

        // Subsystem references (all procedurally created at runtime).
        public Canvas MainCanvas { get; private set; }
        public PlayerController Player { get; private set; }
        public WaveManagerAlpha WaveManager { get; private set; }
        public HealthTracker Health { get; private set; }
        public MainMenuUI MainMenu { get; private set; }
        public VirtualJoystick Joystick { get; private set; }

        private Transform _platform;

        /// <summary>
        /// Zero-asset bootstrap. Creates the GameCore host object automatically
        /// when the scene starts, so no manual scene setup is required.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var host = new GameObject("GameCore");
            host.AddComponent<GameCore>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Currency = startingCurrency;
        }

        private void Start()
        {
            EnsureCamera();
            EnsureLighting();
            BuildEnvironment();
            BuildPlayer();
            BuildCanvas();
            BuildSubsystems();

            // Boot directly into the Main Menu with the combat layer frozen.
            EnterMainMenu();
        }

        #region Scene Infrastructure

        private void EnsureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camObj = new GameObject("MainCamera");
                camObj.tag = "MainCamera";
                cam = camObj.AddComponent<Camera>();
            }

            // Angled top-down tabletop view of the diorama.
            cam.transform.position = new Vector3(0f, 18f, -13f);
            cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f, 1f);
            cam.orthographic = false;
            cam.fieldOfView = 60f;
        }

        private void EnsureLighting()
        {
            if (FindObjectOfType<Light>() == null)
            {
                var lightObj = new GameObject("DirectionalLight");
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.96f, 0.9f, 1f);
                light.intensity = 1.1f;
                lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f, 1f);
        }

        /// <summary>
        /// Spawns the scaled tabletop diorama platform at the world origin.
        /// </summary>
        private void BuildEnvironment()
        {
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "DioramaPlatform";
            platform.transform.position = Vector3.zero;
            platform.transform.localScale = new Vector3(30f, 0.5f, 30f);

            var rend = platform.GetComponent<Renderer>();
            rend.material = MakeMaterial(new Color(0.18f, 0.22f, 0.28f, 1f));

            _platform = platform.transform;

            // Decorative raised border so the figurines read as on a tabletop.
            CreateBorderStrip("Border_N", new Vector3(0f, 0.4f, 15f), new Vector3(30f, 1f, 0.6f));
            CreateBorderStrip("Border_S", new Vector3(0f, 0.4f, -15f), new Vector3(30f, 1f, 0.6f));
            CreateBorderStrip("Border_E", new Vector3(15f, 0.4f, 0f), new Vector3(0.6f, 1f, 30f));
            CreateBorderStrip("Border_W", new Vector3(-15f, 0.4f, 0f), new Vector3(0.6f, 1f, 30f));
        }

        private void CreateBorderStrip(string id, Vector3 pos, Vector3 scale)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = id;
            strip.transform.SetParent(_platform != null ? null : null);
            strip.transform.position = pos;
            strip.transform.localScale = scale;
            strip.GetComponent<Renderer>().material = MakeMaterial(new Color(0.30f, 0.34f, 0.42f, 1f));
            // Borders are visual only; remove collider to avoid trapping units.
            Destroy(strip.GetComponent<Collider>());
        }

        private void BuildPlayer()
        {
            var playerObj = new GameObject("Player");
            playerObj.transform.position = new Vector3(0f, 1.25f, 0f);
            Player = playerObj.AddComponent<PlayerController>();
            Player.Initialize(this);
        }

        private void BuildCanvas()
        {
            var canvasObj = new GameObject("MainCanvas");
            MainCanvas = canvasObj.AddComponent<Canvas>();
            MainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Reference resolution chosen to support both Landscape and Portrait.
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            EnsureEventSystem();
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        private void BuildSubsystems()
        {
            // Health / score tracker (builds its own HUD on the canvas).
            var healthObj = new GameObject("HealthTracker");
            Health = healthObj.AddComponent<HealthTracker>();
            Health.Initialize(this);

            // Virtual joystick input controller (fading HUD element).
            var joystickObj = new GameObject("VirtualJoystick");
            Joystick = joystickObj.AddComponent<VirtualJoystick>();
            Joystick.Initialize(this);
            Player.AttachJoystick(Joystick);

            // Wave manager (enemy spawning).
            var waveObj = new GameObject("WaveManagerAlpha");
            WaveManager = waveObj.AddComponent<WaveManagerAlpha>();
            WaveManager.Initialize(this);

            // Main menu (built last so it draws above the HUD).
            var menuObj = new GameObject("MainMenuUI");
            MainMenu = menuObj.AddComponent<MainMenuUI>();
            MainMenu.Initialize(this);
        }

        #endregion

        #region State Machine

        public void EnterMainMenu()
        {
            State = GameState.MainMenu;
            CombatFrozen = true;

            if (MainMenu != null)
            {
                MainMenu.Show();
            }

            if (Joystick != null)
            {
                Joystick.SetInteractable(false);
            }
        }

        /// <summary>
        /// Triggered by the START RUN button. Transitions into active combat.
        /// </summary>
        public void StartRun()
        {
            if (State == GameState.ActiveWave)
            {
                return;
            }

            State = GameState.ActiveWave;
            CombatFrozen = false;

            if (Player != null)
            {
                Player.ResetForRun();
            }

            if (Health != null)
            {
                Health.ResetForRun();
            }

            if (Joystick != null)
            {
                Joystick.SetInteractable(true);
            }

            if (WaveManager != null)
            {
                WaveManager.BeginWaves();
            }
        }

        public void TogglePause()
        {
            if (State == GameState.ActiveWave)
            {
                State = GameState.Paused;
                CombatFrozen = true;
            }
            else if (State == GameState.Paused)
            {
                State = GameState.ActiveWave;
                CombatFrozen = false;
            }
        }

        public void OnPlayerDeath()
        {
            State = GameState.MainMenu;
            CombatFrozen = true;

            if (WaveManager != null)
            {
                WaveManager.StopWaves();
            }

            if (Joystick != null)
            {
                Joystick.SetInteractable(false);
            }

            if (MainMenu != null)
            {
                MainMenu.Show();
            }
        }

        public void AddCurrency(int amount)
        {
            Currency = Mathf.Max(0, Currency + amount);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a lightweight unlit-style material at runtime. Falls back across
        /// shader names so it works in Built-in, URP, and HDRP pipelines.
        /// </summary>
        public static Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            return mat;
        }

        #endregion
    }
}
