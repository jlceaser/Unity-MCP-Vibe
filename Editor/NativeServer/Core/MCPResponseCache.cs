using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.NativeServer.Protocol;

namespace MCPForUnity.Editor.NativeServer.Core
{
    /// <summary>
    /// Response cache for read-only MCP tools.
    /// Automatically caches responses for tools matching read-only patterns.
    /// </summary>
    public class MCPResponseCache
    {
        // Cache entry with expiration and ETag
        private class CacheEntry
        {
            public MCPToolResult Result { get; set; }
            public DateTime ExpiresAt { get; set; }
            public string ETag { get; set; }
            public string ArgsHash { get; set; }
        }

        // Main cache storage
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

        // Default TTL values (in seconds)
        private const int DefaultTtlSeconds = 30;
        private const int LongTtlSeconds = 300; // 5 minutes for stable data

        // Patterns for read-only tools that should be cached
        private static readonly Regex ReadOnlyPattern = new Regex(
            @"^(get_|list_|find_|search_|query_|check_|.*_info$|.*_status$|.*_list$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Patterns for tools that should never be cached
        private static readonly Regex NoCachePattern = new Regex(
            @"^(create_|delete_|update_|modify_|set_|add_|remove_|execute_|run_|apply_|save_|write_)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Custom TTL overrides per tool
        private readonly Dictionary<string, int> _customTtl = new()
        {
            { "get_project_info", LongTtlSeconds },
            { "list_tools", LongTtlSeconds },
            { "get_scene_info", 60 },
            { "list_assets", 60 },
            { "get_console_logs", 5 }, // Short TTL for frequently changing data
        };

        /// <summary>
        /// Try to get a cached response for a tool call
        /// </summary>
        public bool TryGetCachedResponse(string toolName, JObject arguments, out MCPToolResult result, out string etag)
        {
            result = null;
            etag = null;

            if (!ShouldCache(toolName))
            {
                return false;
            }

            string cacheKey = GenerateCacheKey(toolName, arguments);
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                // Check if entry has expired
                if (DateTime.UtcNow < entry.ExpiresAt)
                {
                    result = entry.Result;
                    etag = entry.ETag;
                    return true;
                }
                else
                {
                    // Entry expired, remove it
                    _cache.TryRemove(cacheKey, out _);
                }
            }

            return false;
        }

        /// <summary>
        /// Cache a response for a tool call
        /// </summary>
        public void CacheResponse(string toolName, JObject arguments, MCPToolResult result)
        {
            if (!ShouldCache(toolName))
            {
                return;
            }

            // Don't cache error responses
            if (result.IsError)
            {
                return;
            }

            string cacheKey = GenerateCacheKey(toolName, arguments);
            int ttlSeconds = GetTtlForTool(toolName);

            var entry = new CacheEntry
            {
                Result = result,
                ExpiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds),
                ETag = GenerateETag(result),
                ArgsHash = HashArguments(arguments)
            };

            _cache[cacheKey] = entry;
        }

        /// <summary>
        /// Check if a cached response is still valid by ETag
        /// </summary>
        public bool ValidateETag(string toolName, JObject arguments, string clientETag)
        {
            string cacheKey = GenerateCacheKey(toolName, arguments);
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                return entry.ETag == clientETag && DateTime.UtcNow < entry.ExpiresAt;
            }
            return false;
        }

        /// <summary>
        /// Invalidate cache for a specific tool
        /// </summary>
        public void InvalidateTool(string toolName)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in _cache)
            {
                if (kvp.Key.StartsWith(toolName + ":"))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Invalidate cache for tools matching a pattern
        /// </summary>
        public void InvalidatePattern(string pattern)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var keysToRemove = new List<string>();
            foreach (var kvp in _cache)
            {
                if (regex.IsMatch(kvp.Key))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Clear all cached responses
        /// </summary>
        public void ClearAll()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public Dictionary<string, object> GetStats()
        {
            int total = _cache.Count;
            int expired = 0;
            var toolCounts = new Dictionary<string, int>();

            foreach (var kvp in _cache)
            {
                if (DateTime.UtcNow >= kvp.Value.ExpiresAt)
                {
                    expired++;
                }

                string toolName = kvp.Key.Split(':')[0];
                if (!toolCounts.ContainsKey(toolName))
                {
                    toolCounts[toolName] = 0;
                }
                toolCounts[toolName]++;
            }

            return new Dictionary<string, object>
            {
                ["total_entries"] = total,
                ["expired_entries"] = expired,
                ["valid_entries"] = total - expired,
                ["tools_cached"] = toolCounts
            };
        }

        /// <summary>
        /// Determine if a tool should be cached
        /// </summary>
        public bool ShouldCache(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            // Never cache mutation tools
            if (NoCachePattern.IsMatch(toolName))
            {
                return false;
            }

            // Cache read-only tools
            return ReadOnlyPattern.IsMatch(toolName);
        }

        /// <summary>
        /// Set custom TTL for a specific tool
        /// </summary>
        public void SetToolTtl(string toolName, int ttlSeconds)
        {
            _customTtl[toolName] = ttlSeconds;
        }

        private int GetTtlForTool(string toolName)
        {
            if (_customTtl.TryGetValue(toolName, out int ttl))
            {
                return ttl;
            }
            return DefaultTtlSeconds;
        }

        private string GenerateCacheKey(string toolName, JObject arguments)
        {
            string argsHash = HashArguments(arguments);
            return $"{toolName}:{argsHash}";
        }

        private string HashArguments(JObject arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "empty";
            }

            // Sort keys for consistent hashing
            string json = JsonConvert.SerializeObject(arguments, new JsonSerializerSettings
            {
                Formatting = Formatting.None
            });

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(json));
                return Convert.ToBase64String(hash).Substring(0, 8);
            }
        }

        private string GenerateETag(MCPToolResult result)
        {
            if (result?.Content == null || result.Content.Count == 0)
            {
                return "W/\"empty\"";
            }

            // Generate ETag from content hash
            string contentJson = JsonConvert.SerializeObject(result.Content);
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(contentJson));
                string hashStr = Convert.ToBase64String(hash).Substring(0, 12);
                return $"W/\"{hashStr}\"";
            }
        }
    }
}
