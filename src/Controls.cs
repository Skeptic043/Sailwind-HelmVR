using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace HelmVR
{
    /// <summary>Physical controller buttons, named as printed on Quest Touch controllers.</summary>
    public enum ControllerButton
    {
        None = 0,
        A = 1,
        B = 2,
        X = 3,
        Y = 4,
        LeftStickClick = 5,
        RightStickClick = 6
    }

    internal static class ControllerButtons
    {
        /// <summary>mode 0 = held, 1 = pressed this frame, 2 = released this frame.</summary>
        internal static bool Check(ControllerButton button, HandState left, HandState right, int mode)
        {
            switch (button)
            {
                case ControllerButton.A: return Pick(mode, right.BottomFacePressed, right.BottomFaceDown, right.BottomFaceUp);
                case ControllerButton.B: return Pick(mode, right.TopFacePressed, right.TopFaceDown, right.TopFaceUp);
                case ControllerButton.X: return Pick(mode, left.BottomFacePressed, left.BottomFaceDown, left.BottomFaceUp);
                case ControllerButton.Y: return Pick(mode, left.TopFacePressed, left.TopFaceDown, left.TopFaceUp);
                case ControllerButton.LeftStickClick: return Pick(mode, left.StickClickPressed, left.StickClickDown, left.StickClickUp);
                case ControllerButton.RightStickClick: return Pick(mode, right.StickClickPressed, right.StickClickDown, right.StickClickUp);
                default: return false;
            }
        }

        private static bool Pick(int mode, bool held, bool down, bool up) => mode == 0 ? held : (mode == 1 ? down : up);
    }

    /// <summary>
    /// A keyboard shortcut stored as text such as "LeftShift + F7". Invalid text is kept as typed and the shortcut is
    /// turned off with a warning, instead of BepInEx's KeyboardShortcut converter silently wiping it. Blank means off.
    /// </summary>
    internal sealed class ShortcutSetting
    {
        internal const string Help = " Add modifiers with +, for example LeftShift + F7. Names ignore capitalization and accept spaces. Leave blank to turn it off.";

        private readonly ConfigEntry<string> _entry;
        private KeyboardShortcut _parsed = KeyboardShortcut.Empty;
        private string _lastRaw;

        internal ShortcutSetting(ConfigFile config, string section, string key, string description, string defaultValue = "")
        {
            _entry = config.Bind(section, key, defaultValue, description + Help);
            _entry.SettingChanged += (_, __) => Refresh();
            Refresh();
        }

        /// <summary>mode 0 = held, 1 = pressed this frame, 2 = released this frame. Other held keys don't block it.</summary>
        internal bool Check(int mode)
        {
            KeyCode main = _parsed.MainKey;
            if (main == KeyCode.None) return false;
            bool mainHit = mode == 0 ? Input.GetKey(main) : (mode == 1 ? Input.GetKeyDown(main) : Input.GetKeyUp(main));
            if (!mainHit) return false;
            foreach (KeyCode modifier in _parsed.Modifiers)
                if (!Input.GetKey(modifier)) return false;
            return true;
        }

        private void Refresh()
        {
            string raw = _entry.Value;
            if (string.Equals(raw, _lastRaw, StringComparison.Ordinal)) return;
            _lastRaw = raw;
            if (!ShortcutParser.TryParse(raw, out _parsed, out string error))
            {
                Plugin.Log.LogWarning("[" + _entry.Definition.Section + "] " + _entry.Definition.Key
                    + " is turned off because its shortcut is invalid. The value '" + raw + "' was kept unchanged. " + error);
            }
        }
    }

    internal static class ShortcutParser
    {
        private static readonly Dictionary<string, KeyCode> Names = Enum.GetNames(typeof(KeyCode))
            .ToDictionary(name => name, name => (KeyCode)Enum.Parse(typeof(KeyCode), name), StringComparer.OrdinalIgnoreCase);

        internal static bool TryParse(string raw, out KeyboardShortcut shortcut, out string error)
        {
            shortcut = KeyboardShortcut.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var keys = new List<KeyCode>();
            foreach (string segment in raw.Split(new[] { '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;
                // Resolve spaced names such as "left shift" before trying space-separated keys.
                if (TryKey(segment, out KeyCode key)) keys.Add(key);
                else
                {
                    foreach (string token in segment.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!TryKey(token, out key))
                        {
                            error = "Unknown key name '" + token + "'. Use names such as F7, LeftShift or Mouse4. Separate keys with +.";
                            return false;
                        }
                        keys.Add(key);
                    }
                }
            }
            if (keys.Count == 0)
            {
                error = "No key name was provided. Leave the value blank to turn this shortcut off.";
                return false;
            }
            if (keys.Contains(KeyCode.None))
            {
                if (keys.Count == 1) return true;
                error = "None must be used alone to turn a shortcut off.";
                return false;
            }
            keys = keys.Distinct().ToList();
            var mainKeys = keys.Where(key => !IsModifier(key)).ToArray();
            // A modifier chord fires on its one non-modifier key in either order.
            KeyCode main = mainKeys.Length == 1 ? mainKeys[0] : keys[0];
            shortcut = new KeyboardShortcut(main, keys.Where(key => key != main).ToArray());
            return true;
        }

        private static bool TryKey(string text, out KeyCode key) =>
            Names.TryGetValue(string.Concat(text.Where(character => !char.IsWhiteSpace(character))), out key);

        private static bool IsModifier(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftShift: case KeyCode.RightShift:
                case KeyCode.LeftControl: case KeyCode.RightControl:
                case KeyCode.LeftAlt: case KeyCode.RightAlt:
                case KeyCode.LeftCommand: case KeyCode.RightCommand:
                case KeyCode.LeftWindows: case KeyCode.RightWindows:
                case KeyCode.AltGr:
                    return true;
                default:
                    return false;
            }
        }
    }
}
