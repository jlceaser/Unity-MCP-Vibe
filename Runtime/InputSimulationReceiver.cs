#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Endurance.Runtime
{
    /// <summary>
    /// Receives simulated input from MCP and feeds it to Unity's Input System.
    /// Add this to a GameObject in your scene to enable input simulation during Play Mode.
    /// Note: Full Input System integration requires the Input System package.
    /// </summary>
    public class InputSimulationReceiver : MonoBehaviour
    {
        public static InputSimulationReceiver Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private bool logInputs = false;

        // Simulated state
        private Dictionary<KeyCode, bool> simulatedKeys = new Dictionary<KeyCode, bool>();
        private Dictionary<string, float> simulatedAxes = new Dictionary<string, float>();
        private Vector2 simulatedMousePosition;
        private Dictionary<int, bool> simulatedMouseButtons = new Dictionary<int, bool>();

#if ENABLE_INPUT_SYSTEM
        // Input System devices
        private Keyboard virtualKeyboard;
        private Mouse virtualMouse;
        private Gamepad virtualGamepad;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeVirtualDevices();
        }

        private void InitializeVirtualDevices()
        {
#if ENABLE_INPUT_SYSTEM
            // Use existing devices or create virtual ones
            virtualKeyboard = Keyboard.current;
            virtualMouse = Mouse.current;
            virtualGamepad = Gamepad.current;

            if (logInputs)
                Debug.Log("[InputSimulationReceiver] Initialized with Input System support");
#else
            if (logInputs)
                Debug.Log("[InputSimulationReceiver] Initialized (Legacy Input only - Input System not available)");
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        #region Public API - Called via SendMessage from MCP

        /// <summary>
        /// Simulate key down event
        /// </summary>
        public void OnSimulatedKeyDown(KeyCode keyCode)
        {
            simulatedKeys[keyCode] = true;

            if (logInputs)
                Debug.Log($"[InputSimulation] Key Down: {keyCode}");

#if ENABLE_INPUT_SYSTEM
            TriggerKeyboardEvent(keyCode, true);
#endif
        }

        /// <summary>
        /// Simulate key up event
        /// </summary>
        public void OnSimulatedKeyUp(KeyCode keyCode)
        {
            simulatedKeys[keyCode] = false;

            if (logInputs)
                Debug.Log($"[InputSimulation] Key Up: {keyCode}");

#if ENABLE_INPUT_SYSTEM
            TriggerKeyboardEvent(keyCode, false);
#endif
        }

        /// <summary>
        /// Simulate axis input (like joystick or WASD movement)
        /// </summary>
        public void OnSimulatedAxis(object[] args)
        {
            if (args.Length >= 2)
            {
                string axisName = args[0].ToString();
                float value = Convert.ToSingle(args[1]);
                simulatedAxes[axisName] = value;

                if (logInputs)
                    Debug.Log($"[InputSimulation] Axis: {axisName} = {value}");

#if ENABLE_INPUT_SYSTEM
                TriggerAxisEvent(axisName, value);
#endif
            }
        }

        /// <summary>
        /// Simulate mouse click
        /// </summary>
        public void OnSimulatedMouseClick(int button)
        {
            simulatedMouseButtons[button] = true;

            if (logInputs)
                Debug.Log($"[InputSimulation] Mouse Click: Button {button}");

            // Auto-release after frame
            StartCoroutine(ReleaseMouseButtonNextFrame(button));
        }

        /// <summary>
        /// Simulate mouse position
        /// </summary>
        public void OnSimulatedMouseMove(Vector2 position)
        {
            simulatedMousePosition = position;

            if (logInputs)
                Debug.Log($"[InputSimulation] Mouse Move: {position}");
        }

        #endregion

        #region Input State Queries

        /// <summary>
        /// Check if a simulated key is held
        /// </summary>
        public bool IsKeyHeld(KeyCode keyCode)
        {
            return simulatedKeys.TryGetValue(keyCode, out bool held) && held;
        }

        /// <summary>
        /// Get simulated axis value
        /// </summary>
        public float GetAxis(string axisName)
        {
            return simulatedAxes.TryGetValue(axisName, out float value) ? value : 0f;
        }

        /// <summary>
        /// Get simulated mouse button state
        /// </summary>
        public bool IsMouseButtonHeld(int button)
        {
            return simulatedMouseButtons.TryGetValue(button, out bool held) && held;
        }

        /// <summary>
        /// Get simulated mouse position
        /// </summary>
        public Vector2 GetMousePosition()
        {
            return simulatedMousePosition;
        }

        #endregion

#if ENABLE_INPUT_SYSTEM
        #region Input System Integration

        private void TriggerKeyboardEvent(KeyCode keyCode, bool pressed)
        {
            if (virtualKeyboard == null) return;

            try
            {
                // Map KeyCode to Input System Key
                Key? key = KeyCodeToKey(keyCode);
                if (key.HasValue)
                {
                    using (StateEvent.From(virtualKeyboard, out InputEventPtr eventPtr))
                    {
                        var keyControl = virtualKeyboard[key.Value];
                        keyControl.WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                        InputSystem.QueueEvent(eventPtr);
                    }
                }
            }
            catch (Exception e)
            {
                if (logInputs)
                    Debug.LogWarning($"[InputSimulation] Failed to trigger keyboard event: {e.Message}");
            }
        }

        private void TriggerAxisEvent(string axisName, float value)
        {
            if (virtualGamepad == null) return;

            try
            {
                // Map common axis names to gamepad controls
                using (StateEvent.From(virtualGamepad, out InputEventPtr eventPtr))
                {
                    switch (axisName.ToLower())
                    {
                        case "horizontal":
                            virtualGamepad.leftStick.x.WriteValueIntoEvent(value, eventPtr);
                            break;
                        case "vertical":
                            virtualGamepad.leftStick.y.WriteValueIntoEvent(value, eventPtr);
                            break;
                        case "mouse x":
                        case "righthorizontal":
                            virtualGamepad.rightStick.x.WriteValueIntoEvent(value, eventPtr);
                            break;
                        case "mouse y":
                        case "rightvertical":
                            virtualGamepad.rightStick.y.WriteValueIntoEvent(value, eventPtr);
                            break;
                    }
                    InputSystem.QueueEvent(eventPtr);
                }
            }
            catch (Exception e)
            {
                if (logInputs)
                    Debug.LogWarning($"[InputSimulation] Failed to trigger axis event: {e.Message}");
            }
        }

        private Key? KeyCodeToKey(KeyCode keyCode)
        {
            // Map common KeyCodes to Input System Keys
            switch (keyCode)
            {
                // Letters
                case KeyCode.A: return Key.A;
                case KeyCode.B: return Key.B;
                case KeyCode.C: return Key.C;
                case KeyCode.D: return Key.D;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.I: return Key.I;
                case KeyCode.J: return Key.J;
                case KeyCode.K: return Key.K;
                case KeyCode.L: return Key.L;
                case KeyCode.M: return Key.M;
                case KeyCode.N: return Key.N;
                case KeyCode.O: return Key.O;
                case KeyCode.P: return Key.P;
                case KeyCode.Q: return Key.Q;
                case KeyCode.R: return Key.R;
                case KeyCode.S: return Key.S;
                case KeyCode.T: return Key.T;
                case KeyCode.U: return Key.U;
                case KeyCode.V: return Key.V;
                case KeyCode.W: return Key.W;
                case KeyCode.X: return Key.X;
                case KeyCode.Y: return Key.Y;
                case KeyCode.Z: return Key.Z;

                // Numbers
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2;
                case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4;
                case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.Alpha6: return Key.Digit6;
                case KeyCode.Alpha7: return Key.Digit7;
                case KeyCode.Alpha8: return Key.Digit8;
                case KeyCode.Alpha9: return Key.Digit9;

                // Function keys
                case KeyCode.F1: return Key.F1;
                case KeyCode.F2: return Key.F2;
                case KeyCode.F3: return Key.F3;
                case KeyCode.F4: return Key.F4;
                case KeyCode.F5: return Key.F5;
                case KeyCode.F6: return Key.F6;
                case KeyCode.F7: return Key.F7;
                case KeyCode.F8: return Key.F8;
                case KeyCode.F9: return Key.F9;
                case KeyCode.F10: return Key.F10;
                case KeyCode.F11: return Key.F11;
                case KeyCode.F12: return Key.F12;

                // Special keys
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;

                // Arrow keys
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;

                default:
                    return null;
            }
        }

        #endregion
#endif

        private System.Collections.IEnumerator ReleaseMouseButtonNextFrame(int button)
        {
            yield return null;
            simulatedMouseButtons[button] = false;
        }

        #region Legacy Input Compatibility

        private void Update()
        {
            // For scripts using legacy Input.GetKey(), we provide compatibility
            // They can check InputSimulationReceiver.Instance.IsKeyHeld() instead
        }

        #endregion

        #region Editor Integration

#if UNITY_EDITOR
        [ContextMenu("Test Key Press (W)")]
        private void TestKeyPress()
        {
            OnSimulatedKeyDown(KeyCode.W);
            Invoke(nameof(TestKeyRelease), 0.5f);
        }

        private void TestKeyRelease()
        {
            OnSimulatedKeyUp(KeyCode.W);
        }
#endif

        #endregion
    }
}
