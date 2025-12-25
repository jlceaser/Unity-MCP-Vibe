using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Provides consistent parameter access with fallback support.
    /// Handles parameter naming inconsistencies across MCP tools.
    /// </summary>
    public static class ParameterHelper
    {
        #region Target/Object Name

        /// <summary>
        /// Gets the target object name from parameters.
        /// Supports: target, targetName, objectName, object, name
        /// </summary>
        public static string GetTargetName(JObject @params)
        {
            if (@params == null) return null;

            return @params["target"]?.ToString()
                ?? @params["targetName"]?.ToString()
                ?? @params["objectName"]?.ToString()
                ?? @params["object"]?.ToString()
                ?? @params["name"]?.ToString();
        }

        /// <summary>
        /// Gets the target object name, throws if not found.
        /// </summary>
        public static string RequireTargetName(JObject @params, string toolName = "Tool")
        {
            var name = GetTargetName(@params);
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"{toolName} requires 'target' parameter. " +
                    "Accepted aliases: target, targetName, objectName, object, name");
            }
            return name;
        }

        #endregion

        #region Component Name

        /// <summary>
        /// Gets the component name/type from parameters.
        /// Supports: componentName, component, componentType, targetComponent, type
        /// </summary>
        public static string GetComponentName(JObject @params)
        {
            if (@params == null) return null;

            return @params["componentName"]?.ToString()
                ?? @params["component"]?.ToString()
                ?? @params["componentType"]?.ToString()
                ?? @params["targetComponent"]?.ToString()
                ?? @params["type"]?.ToString();
        }

        /// <summary>
        /// Gets the component name, throws if not found.
        /// </summary>
        public static string RequireComponentName(JObject @params, string toolName = "Tool")
        {
            var name = GetComponentName(@params);
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"{toolName} requires 'componentName' parameter. " +
                    "Accepted aliases: componentName, component, componentType, targetComponent, type");
            }
            return name;
        }

        #endregion

        #region Asset Type

        /// <summary>
        /// Gets the asset type from parameters.
        /// Supports: assetType, asset_type, type, typeName
        /// </summary>
        public static string GetAssetType(JObject @params)
        {
            if (@params == null) return null;

            return @params["assetType"]?.ToString()
                ?? @params["asset_type"]?.ToString()
                ?? @params["type"]?.ToString()
                ?? @params["typeName"]?.ToString();
        }

        #endregion

        #region Path

        /// <summary>
        /// Gets the path from parameters.
        /// Supports: path, assetPath, filePath, folderPath
        /// </summary>
        public static string GetPath(JObject @params)
        {
            if (@params == null) return null;

            return @params["path"]?.ToString()
                ?? @params["assetPath"]?.ToString()
                ?? @params["filePath"]?.ToString()
                ?? @params["folderPath"]?.ToString();
        }

        /// <summary>
        /// Gets the material path from parameters.
        /// Supports: materialPath, path, assetPath
        /// </summary>
        public static string GetMaterialPath(JObject @params)
        {
            if (@params == null) return null;

            return @params["materialPath"]?.ToString()
                ?? @params["path"]?.ToString()
                ?? @params["assetPath"]?.ToString();
        }

        /// <summary>
        /// Gets the prefab path from parameters.
        /// Supports: prefabPath, path, assetPath
        /// </summary>
        public static string GetPrefabPath(JObject @params)
        {
            if (@params == null) return null;

            return @params["prefabPath"]?.ToString()
                ?? @params["path"]?.ToString()
                ?? @params["assetPath"]?.ToString();
        }

        #endregion

        #region Property Name

        /// <summary>
        /// Gets the property name from parameters.
        /// Supports: property, propertyName, prop
        /// </summary>
        public static string GetPropertyName(JObject @params)
        {
            if (@params == null) return null;

            return @params["property"]?.ToString()
                ?? @params["propertyName"]?.ToString()
                ?? @params["prop"]?.ToString();
        }

        #endregion

        #region Value

        /// <summary>
        /// Gets the value from parameters.
        /// Supports: value, newValue, val
        /// </summary>
        public static JToken GetValue(JObject @params)
        {
            if (@params == null) return null;

            return @params["value"]
                ?? @params["newValue"]
                ?? @params["val"];
        }

        /// <summary>
        /// Gets the value as string from parameters.
        /// </summary>
        public static string GetValueAsString(JObject @params)
        {
            return GetValue(@params)?.ToString();
        }

        #endregion

        #region Action

        /// <summary>
        /// Gets the action from parameters (this one is consistent across tools).
        /// </summary>
        public static string GetAction(JObject @params)
        {
            return @params?["action"]?.ToString();
        }

        /// <summary>
        /// Gets the action, throws if not found.
        /// </summary>
        public static string RequireAction(JObject @params, string toolName = "Tool")
        {
            var action = GetAction(@params);
            if (string.IsNullOrEmpty(action))
            {
                throw new ArgumentException($"{toolName} requires 'action' parameter.");
            }
            return action;
        }

        #endregion

        #region Boolean Parameters

        /// <summary>
        /// Gets a boolean parameter with fallback.
        /// </summary>
        public static bool GetBool(JObject @params, string primary, bool defaultValue = false, params string[] aliases)
        {
            if (@params == null) return defaultValue;

            var token = @params[primary];
            if (token != null) return token.Value<bool>();

            foreach (var alias in aliases)
            {
                token = @params[alias];
                if (token != null) return token.Value<bool>();
            }

            return defaultValue;
        }

        #endregion

        #region Numeric Parameters

        /// <summary>
        /// Gets an integer parameter with fallback.
        /// </summary>
        public static int GetInt(JObject @params, string primary, int defaultValue = 0, params string[] aliases)
        {
            if (@params == null) return defaultValue;

            var token = @params[primary];
            if (token != null) return token.Value<int>();

            foreach (var alias in aliases)
            {
                token = @params[alias];
                if (token != null) return token.Value<int>();
            }

            return defaultValue;
        }

        /// <summary>
        /// Gets a float parameter with fallback.
        /// </summary>
        public static float GetFloat(JObject @params, string primary, float defaultValue = 0f, params string[] aliases)
        {
            if (@params == null) return defaultValue;

            var token = @params[primary];
            if (token != null) return token.Value<float>();

            foreach (var alias in aliases)
            {
                token = @params[alias];
                if (token != null) return token.Value<float>();
            }

            return defaultValue;
        }

        #endregion

        #region String with Fallback

        /// <summary>
        /// Gets a string parameter with fallback aliases.
        /// </summary>
        public static string GetString(JObject @params, string primary, params string[] aliases)
        {
            if (@params == null) return null;

            var value = @params[primary]?.ToString();
            if (!string.IsNullOrEmpty(value)) return value;

            foreach (var alias in aliases)
            {
                value = @params[alias]?.ToString();
                if (!string.IsNullOrEmpty(value)) return value;
            }

            return null;
        }

        #endregion

        #region Vector3

        /// <summary>
        /// Gets a Vector3 from parameters.
        /// Supports: position, pos, vector, vec, or x/y/z components
        /// </summary>
        public static Vector3? GetVector3(JObject @params, string primary = "position")
        {
            if (@params == null) return null;

            // Try array format [x, y, z]
            var arrayToken = @params[primary] ?? @params["pos"] ?? @params["vector"] ?? @params["vec"];
            if (arrayToken is JArray arr && arr.Count >= 3)
            {
                return new Vector3(
                    arr[0].Value<float>(),
                    arr[1].Value<float>(),
                    arr[2].Value<float>()
                );
            }

            // Try object format { x: 0, y: 0, z: 0 }
            if (arrayToken is JObject obj)
            {
                return new Vector3(
                    obj["x"]?.Value<float>() ?? 0f,
                    obj["y"]?.Value<float>() ?? 0f,
                    obj["z"]?.Value<float>() ?? 0f
                );
            }

            // Try separate x, y, z parameters
            if (@params["x"] != null || @params["y"] != null || @params["z"] != null)
            {
                return new Vector3(
                    @params["x"]?.Value<float>() ?? 0f,
                    @params["y"]?.Value<float>() ?? 0f,
                    @params["z"]?.Value<float>() ?? 0f
                );
            }

            return null;
        }

        #endregion

        #region Color

        /// <summary>
        /// Gets a Color from parameters.
        /// Supports: color, col, or r/g/b/a components, or hex string
        /// </summary>
        public static Color? GetColor(JObject @params, string primary = "color")
        {
            if (@params == null) return null;

            var token = @params[primary] ?? @params["col"];

            // Try hex string "#RRGGBB" or "#RRGGBBAA"
            if (token?.Type == JTokenType.String)
            {
                var hex = token.ToString();
                if (ColorUtility.TryParseHtmlString(hex, out Color c))
                {
                    return c;
                }
            }

            // Try array format [r, g, b] or [r, g, b, a]
            if (token is JArray arr && arr.Count >= 3)
            {
                return new Color(
                    arr[0].Value<float>(),
                    arr[1].Value<float>(),
                    arr[2].Value<float>(),
                    arr.Count > 3 ? arr[3].Value<float>() : 1f
                );
            }

            // Try object format { r: 0, g: 0, b: 0, a: 1 }
            if (token is JObject obj)
            {
                return new Color(
                    obj["r"]?.Value<float>() ?? 0f,
                    obj["g"]?.Value<float>() ?? 0f,
                    obj["b"]?.Value<float>() ?? 0f,
                    obj["a"]?.Value<float>() ?? 1f
                );
            }

            // Try separate r, g, b, a parameters
            if (@params["r"] != null || @params["g"] != null || @params["b"] != null)
            {
                return new Color(
                    @params["r"]?.Value<float>() ?? 0f,
                    @params["g"]?.Value<float>() ?? 0f,
                    @params["b"]?.Value<float>() ?? 0f,
                    @params["a"]?.Value<float>() ?? 1f
                );
            }

            return null;
        }

        #endregion
    }
}
