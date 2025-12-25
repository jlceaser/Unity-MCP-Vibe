using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.NativeServer.Core
{
    /// <summary>
    /// Generates tool metadata cache at compile time.
    /// This eliminates runtime reflection overhead for tool discovery.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPToolMetadataGenerator
    {
        private static bool _isGenerating;

        static MCPToolMetadataGenerator()
        {
            // Delay generation to avoid issues during domain reload
            EditorApplication.delayCall += TryGenerateCache;
        }

        private static void TryGenerateCache()
        {
            if (_isGenerating) return;

            // Check if regeneration is needed
            var cache = LoadCache();
            if (cache != null && cache.UnityVersion == Application.unityVersion)
            {
                // Cache exists and matches Unity version - check if tool count changed
                int currentCount = CountToolTypes();
                if (cache.ToolCount == currentCount)
                {
                    return; // Cache is valid
                }
            }

            GenerateCache();
        }

        public static void GenerateCache()
        {
            if (_isGenerating)
            {
                McpLog.Warn("[MCPToolMetadataGenerator] Generation already in progress");
                return;
            }

            _isGenerating = true;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var tools = DiscoverAllTools();
                sw.Stop();

                // Create or update cache asset
                var cache = LoadOrCreateCache();
                cache.SetTools(tools);

                EditorUtility.SetDirty(cache);
                AssetDatabase.SaveAssets();

                McpLog.Info($"[MCPToolMetadataGenerator] Generated cache with {tools.Count} tools in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                McpLog.Error($"[MCPToolMetadataGenerator] Failed to generate cache: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private static MCPToolMetadataCache LoadCache()
        {
            return AssetDatabase.LoadAssetAtPath<MCPToolMetadataCache>(MCPToolMetadataCache.GetAssetPath());
        }

        private static MCPToolMetadataCache LoadOrCreateCache()
        {
            var cache = LoadCache();
            if (cache != null) return cache;

            // Create new cache asset
            cache = ScriptableObject.CreateInstance<MCPToolMetadataCache>();

            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(MCPToolMetadataCache.GetAssetPath());
            if (!AssetDatabase.IsValidFolder(directory))
            {
                // Create Resources folder if needed
                string parentDir = System.IO.Path.GetDirectoryName(directory);
                AssetDatabase.CreateFolder(parentDir, "Resources");
            }

            AssetDatabase.CreateAsset(cache, MCPToolMetadataCache.GetAssetPath());
            return cache;
        }

        private static int CountToolTypes()
        {
            int count = 0;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (ShouldSkipAssembly(assembly)) continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.GetCustomAttribute<McpForUnityToolAttribute>() != null)
                        {
                            count++;
                        }
                    }
                }
                catch { }
            }
            return count;
        }

        private static List<MCPToolMetadataCache.CachedToolEntry> DiscoverAllTools()
        {
            var tools = new List<MCPToolMetadataCache.CachedToolEntry>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (ShouldSkipAssembly(assembly)) continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var toolAttr = type.GetCustomAttribute<McpForUnityToolAttribute>();
                        if (toolAttr == null) continue;

                        var entry = new MCPToolMetadataCache.CachedToolEntry
                        {
                            Name = toolAttr.Name ?? type.Name.ToLower(),
                            Description = toolAttr.Description ?? "",
                            ClassName = type.Name,
                            Namespace = type.Namespace ?? "",
                            AssemblyName = assembly.GetName().Name,
                            FullTypeName = type.AssemblyQualifiedName,
                            AutoRegister = toolAttr.AutoRegister,
                            RequiresPolling = toolAttr.RequiresPolling,
                            PollAction = toolAttr.PollAction,
                            StructuredOutput = toolAttr.StructuredOutput,
                            Parameters = ExtractParameters(type)
                        };

                        tools.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    // Skip problematic assemblies
                    if (ex is not ReflectionTypeLoadException)
                    {
                        McpLog.Warn($"[MCPToolMetadataGenerator] Failed to scan assembly {assembly.GetName().Name}: {ex.Message}");
                    }
                }
            }

            return tools;
        }

        private static List<MCPToolMetadataCache.CachedParameter> ExtractParameters(Type type)
        {
            var parameters = new List<MCPToolMetadataCache.CachedParameter>();

            // Look for nested Parameters class
            var parametersType = type.GetNestedType("Parameters", BindingFlags.Public | BindingFlags.NonPublic);
            if (parametersType == null) return parameters;

            foreach (var prop in parametersType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var paramAttr = prop.GetCustomAttribute<ToolParameterAttribute>();

                parameters.Add(new MCPToolMetadataCache.CachedParameter
                {
                    Name = prop.Name,
                    Description = paramAttr?.Description ?? "",
                    Type = GetParameterTypeName(prop.PropertyType),
                    Required = paramAttr?.Required ?? false,
                    DefaultValue = paramAttr?.DefaultValue
                });
            }

            return parameters;
        }

        private static string GetParameterTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(int?)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(float?) || type == typeof(double?)) return "number";
            if (type == typeof(bool) || type == typeof(bool?)) return "boolean";
            if (type.IsArray) return "array";
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "array";
            return "object";
        }

        private static bool ShouldSkipAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic) return true;

            string name = assembly.GetName().Name;
            return name.StartsWith("System") ||
                   name.StartsWith("mscorlib") ||
                   name.StartsWith("Unity.") ||
                   name.StartsWith("UnityEngine") ||
                   name.StartsWith("UnityEditor") ||
                   name.StartsWith("Mono.") ||
                   name.StartsWith("netstandard") ||
                   name.StartsWith("Microsoft.");
        }
    }
}
