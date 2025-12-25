#nullable disable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.NativeServer.Protocol;

namespace MCPForUnity.Editor.NativeServer.Core
{
    /// <summary>
    /// Tool handler delegate
    /// </summary>
    public delegate Task<MCPToolResult> MCPToolHandler(JObject arguments);

    /// <summary>
    /// Synchronous tool handler delegate
    /// </summary>
    public delegate object MCPSyncToolHandler(JObject arguments);

    /// <summary>
    /// Tool registration info
    /// </summary>
    public class MCPToolRegistration
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JObject InputSchema { get; set; }
        public MCPToolHandler Handler { get; set; }
        public bool RequiresMainThread { get; set; } = true;
        public string Category { get; set; }
        public DateTime RegisteredAt { get; set; }
        public int CallCount { get; set; }
        public double TotalExecutionTime { get; set; }
    }

    /// <summary>
    /// High-performance tool registry for MCP.
    /// Automatically discovers and registers tools from assemblies.
    /// </summary>
    public class MCPToolRegistry
    {
        private readonly ConcurrentDictionary<string, MCPToolRegistration> _tools;
        private readonly object _lock = new object();

        public int ToolCount => _tools.Count;
        public IEnumerable<string> ToolNames => _tools.Keys;

        public event Action<string> OnToolRegistered;
        public event Action<string, double> OnToolExecuted;
        public event Action<string, Exception> OnToolError;

        public MCPToolRegistry()
        {
            _tools = new ConcurrentDictionary<string, MCPToolRegistration>(StringComparer.OrdinalIgnoreCase);
        }

        #region Registration

        /// <summary>
        /// Register a tool with async handler
        /// </summary>
        public void RegisterTool(string name, string description, MCPToolHandler handler, JObject inputSchema = null, string category = null)
        {
            var registration = new MCPToolRegistration
            {
                Name = name,
                Description = description,
                Handler = handler,
                InputSchema = inputSchema ?? CreateDefaultSchema(),
                Category = category ?? "general",
                RegisteredAt = DateTime.UtcNow
            };

            _tools[name] = registration;
            OnToolRegistered?.Invoke(name);
        }

        /// <summary>
        /// Register a tool with sync handler (will be wrapped in Task)
        /// </summary>
        public void RegisterTool(string name, string description, MCPSyncToolHandler syncHandler, JObject inputSchema = null, string category = null)
        {
            MCPToolHandler asyncHandler = (args) =>
            {
                object result = syncHandler(args);
                return Task.FromResult(ConvertToToolResult(result));
            };

            RegisterTool(name, description, asyncHandler, inputSchema, category);
        }

        /// <summary>
        /// Register a tool with Func<JObject, object> handler
        /// </summary>
        public void RegisterTool(string name, string description, Func<JObject, object> handler, JObject inputSchema = null, string category = null)
        {
            RegisterTool(name, description, (MCPSyncToolHandler)(args => handler(args)), inputSchema, category);
        }

        /// <summary>
        /// Unregister a tool
        /// </summary>
        public bool UnregisterTool(string name)
        {
            return _tools.TryRemove(name, out _);
        }

        /// <summary>
        /// Check if tool exists
        /// </summary>
        public bool HasTool(string name)
        {
            return _tools.ContainsKey(name);
        }

        /// <summary>
        /// Get tool registration
        /// </summary>
        public MCPToolRegistration GetTool(string name)
        {
            _tools.TryGetValue(name, out var tool);
            return tool;
        }

        #endregion

        #region Auto-Discovery

        /// <summary>
        /// Auto-discover and register tools from assemblies using McpForUnityTool attribute
        /// </summary>
        public int DiscoverTools()
        {
            int count = 0;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    if (assembly.IsDynamic) continue;
                    if (assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("mscorlib")) continue;

                    count += DiscoverToolsInAssembly(assembly);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCPToolRegistry] Failed to scan assembly {assembly.FullName}: {ex.Message}");
                }
            }

            Debug.Log($"[MCPToolRegistry] Discovered {count} tools from assemblies");
            return count;
        }

        private int DiscoverToolsInAssembly(Assembly assembly)
        {
            int count = 0;

            try
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    // Look for McpForUnityTool attribute
                    var toolAttr = type.GetCustomAttributes()
                        .FirstOrDefault(a => a.GetType().Name == "McpForUnityToolAttribute");

                    if (toolAttr != null)
                    {
                        if (RegisterToolFromType(type, toolAttr))
                        {
                            count++;
                        }
                    }
                }
            }
            catch { }

            return count;
        }

        private bool RegisterToolFromType(Type type, Attribute toolAttr)
        {
            try
            {
                // Get Name and Description from attribute
                var nameProperty = toolAttr.GetType().GetProperty("Name");
                var descProperty = toolAttr.GetType().GetProperty("Description");

                string name = nameProperty?.GetValue(toolAttr) as string ?? type.Name.ToLower();
                string description = descProperty?.GetValue(toolAttr) as string ?? $"Tool: {type.Name}";

                // Find HandleCommand method
                var handleMethod = type.GetMethod("HandleCommand", BindingFlags.Public | BindingFlags.Static);
                if (handleMethod == null)
                {
                    Debug.LogWarning($"[MCPToolRegistry] Tool {name} has no HandleCommand method");
                    return false;
                }

                // Create handler
                MCPSyncToolHandler handler = (args) =>
                {
                    try
                    {
                        return handleMethod.Invoke(null, new object[] { args });
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw ex.InnerException ?? ex;
                    }
                };

                RegisterTool(name, description, handler, category: "discovered");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCPToolRegistry] Failed to register tool from {type.Name}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Execution

        /// <summary>
        /// Execute a tool by name
        /// </summary>
        public async Task<MCPToolResult> ExecuteTool(string name, JObject arguments)
        {
            if (!_tools.TryGetValue(name, out var registration))
            {
                return new MCPToolResult
                {
                    IsError = true,
                    Content = new List<MCPContent>
                    {
                        MCPContent.TextContent($"Tool not found: {name}")
                    }
                };
            }

            var startTime = DateTime.UtcNow;

            try
            {
                MCPToolResult result;

                if (registration.RequiresMainThread)
                {
                    // Execute on main thread
                    result = await ExecuteOnMainThread(registration.Handler, arguments);
                }
                else
                {
                    result = await registration.Handler(arguments);
                }

                // Update stats
                registration.CallCount++;
                registration.TotalExecutionTime += (DateTime.UtcNow - startTime).TotalMilliseconds;

                OnToolExecuted?.Invoke(name, (DateTime.UtcNow - startTime).TotalMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                OnToolError?.Invoke(name, ex);

                return new MCPToolResult
                {
                    IsError = true,
                    Content = new List<MCPContent>
                    {
                        MCPContent.TextContent($"Tool execution failed: {ex.Message}")
                    }
                };
            }
        }

        // Queue for main thread execution
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private static bool _updateRegistered = false;

        private Task<MCPToolResult> ExecuteOnMainThread(MCPToolHandler handler, JObject arguments)
        {
            var tcs = new TaskCompletionSource<MCPToolResult>();

            // Ensure update callback is registered
            EnsureUpdateRegistered();

            // Queue the work
            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    var task = handler(arguments);
                    task.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            tcs.SetException(t.Exception.InnerException ?? t.Exception);
                        else
                            tcs.SetResult(t.Result);
                    });
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        private void EnsureUpdateRegistered()
        {
            if (_updateRegistered) return;
            _updateRegistered = true;

            UnityEditor.EditorApplication.update += ProcessMainThreadQueue;

            // Also register for quitting to cleanup
            UnityEditor.EditorApplication.quitting += () => _updateRegistered = false;
        }

        /// <summary>
        /// Force Unity to process queued requests (call from HTTP handler)
        /// </summary>
        public static void RequestUpdate()
        {
            // Try multiple methods to wake up Unity
            try
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
            catch { }

            // Also use delayCall as backup
            UnityEditor.EditorApplication.delayCall += () => { };
        }

        private const int MaxItemsPerFrame = 50; // Increased from 10 for better throughput

        private static void ProcessMainThreadQueue()
        {
            // Process queued actions with configurable limit
            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out var action) && processed < MaxItemsPerFrame)
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MCPToolRegistry] Main thread execution error: {ex.Message}");
                }
                processed++;
            }
        }

        #endregion

        #region List Tools

        /// <summary>
        /// Get all tools as MCP tool definitions
        /// </summary>
        public List<MCPTool> GetAllTools()
        {
            return _tools.Values.Select(t => new MCPTool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema
            }).ToList();
        }

        /// <summary>
        /// Get tools by category
        /// </summary>
        public List<MCPTool> GetToolsByCategory(string category)
        {
            return _tools.Values
                .Where(t => t.Category == category)
                .Select(t => new MCPTool
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = t.InputSchema
                }).ToList();
        }

        /// <summary>
        /// Get tool statistics
        /// </summary>
        public Dictionary<string, object> GetStats()
        {
            return new Dictionary<string, object>
            {
                ["total_tools"] = _tools.Count,
                ["total_calls"] = _tools.Values.Sum(t => t.CallCount),
                ["categories"] = _tools.Values.GroupBy(t => t.Category).ToDictionary(g => g.Key, g => g.Count()),
                ["most_used"] = _tools.Values.OrderByDescending(t => t.CallCount).Take(10).Select(t => new { t.Name, t.CallCount }).ToList()
            };
        }

        #endregion

        #region Helpers

        private MCPToolResult ConvertToToolResult(object result)
        {
            if (result == null)
            {
                return new MCPToolResult
                {
                    Content = new List<MCPContent> { MCPContent.TextContent("null") }
                };
            }

            if (result is MCPToolResult toolResult)
            {
                return toolResult;
            }

            if (result is string str)
            {
                return new MCPToolResult
                {
                    Content = new List<MCPContent> { MCPContent.TextContent(str) }
                };
            }

            // FAST PATH: Direct type checks for known response types (no reflection)
            if (result is MCPForUnity.Editor.Helpers.SuccessResponse successResp)
            {
                return new MCPToolResult
                {
                    IsError = false,
                    Content = new List<MCPContent> { MCPContent.JsonContent(result) }
                };
            }

            if (result is MCPForUnity.Editor.Helpers.ErrorResponse errorResp)
            {
                return new MCPToolResult
                {
                    IsError = true,
                    Content = new List<MCPContent> { MCPContent.JsonContent(result) }
                };
            }

            if (result is MCPForUnity.Editor.Helpers.PendingResponse)
            {
                return new MCPToolResult
                {
                    IsError = false,
                    Content = new List<MCPContent> { MCPContent.JsonContent(result) }
                };
            }

            // Check IMcpResponse interface (still fast, no property reflection)
            if (result is MCPForUnity.Editor.Helpers.IMcpResponse mcpResponse)
            {
                return new MCPToolResult
                {
                    IsError = !mcpResponse.Success,
                    Content = new List<MCPContent> { MCPContent.JsonContent(result) }
                };
            }

            // SLOW PATH: Fallback to reflection for unknown types
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success") ?? resultType.GetProperty("Success");
            var errorProp = resultType.GetProperty("error") ?? resultType.GetProperty("Error");

            bool isError = false;
            if (successProp != null)
            {
                var success = successProp.GetValue(result);
                isError = success is bool b && !b;
            }
            if (errorProp != null)
            {
                var error = errorProp.GetValue(result);
                isError = error != null && !string.IsNullOrEmpty(error.ToString());
            }

            return new MCPToolResult
            {
                IsError = isError,
                Content = new List<MCPContent>
                {
                    MCPContent.JsonContent(result)
                }
            };
        }

        private JObject CreateDefaultSchema()
        {
            return JObject.FromObject(new
            {
                type = "object",
                properties = new { },
                additionalProperties = true
            });
        }

        #endregion
    }
}
