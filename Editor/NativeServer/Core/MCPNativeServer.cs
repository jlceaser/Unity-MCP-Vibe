#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.NativeServer.Protocol;
using MCPForUnity.Editor.NativeServer.Transport;

namespace MCPForUnity.Editor.NativeServer.Core
{
    /// <summary>
    /// Unity MCP Vibe Native Server - JET EDITION
    /// The main controller that orchestrates HTTP server, tools, and resources.
    /// 100% C# - Zero Python dependency - Maximum performance!
    ///
    /// Features:
    /// - Auto-restart on crash
    /// - Health monitoring
    /// - Performance metrics
    /// - Unity Auth error handling
    /// - Focus-free operation
    /// </summary>
    [InitializeOnLoad]
    public class MCPNativeServer : IDisposable
    {
        public const string VERSION = "3.0.0-JET";
        public const string SERVER_NAME = "Unity-MCP-Vibe-Native-JET";
        public const string PROTOCOL_VERSION = "2024-11-05";

        private static MCPNativeServer _instance;
        public static MCPNativeServer Instance => _instance ??= new MCPNativeServer();

        private MCPHttpServer _httpServer;
        private MCPToolRegistry _toolRegistry;
        private MCPResourceRegistry _resourceRegistry;
        private System.Timers.Timer _backgroundTimer;
        private System.Timers.Timer _healthCheckTimer;

        private bool _initialized;
        private DateTime _startTime;
        private int _restartCount = 0;
        private DateTime _lastRestartTime = DateTime.MinValue;

        // Performance metrics
        private long _totalRequests = 0;
        private long _totalToolCalls = 0;
        private double _totalResponseTime = 0;
        private readonly ConcurrentQueue<double> _recentResponseTimes = new ConcurrentQueue<double>();
        private const int MaxRecentResponses = 100;

        // Thread-safe cached values for EditorPrefs (to avoid main thread requirement)
        private static bool _cachedAutoStart = false;
        private static bool _cachedAutoRestart = true;
        private static int _cachedMaxRestartAttempts = 5;
        private static int _cachedRestartCooldownSeconds = 60;
        private static bool _cachedWasRunningBeforeReload = false;
        private static bool _cacheInitialized = false;

        // Initialize cache on main thread
        private static void EnsureCacheInitialized()
        {
            if (_cacheInitialized) return;
            try
            {
                _cachedAutoStart = EditorPrefs.GetBool("MCP_Native_AutoStart", false);
                _cachedAutoRestart = EditorPrefs.GetBool("MCP_Native_AutoRestart", true);
                _cachedMaxRestartAttempts = EditorPrefs.GetInt("MCP_Native_MaxRestarts", 5);
                _cachedRestartCooldownSeconds = EditorPrefs.GetInt("MCP_Native_RestartCooldown", 60);
                _cachedWasRunningBeforeReload = EditorPrefs.GetBool("MCP_Native_WasRunning", false);
                _cacheInitialized = true;
            }
            catch
            {
                // Not on main thread, use defaults
            }
        }

        // Configuration
        public int Port { get; private set; } = 8080;

        public static bool AutoStart
        {
            get { EnsureCacheInitialized(); return _cachedAutoStart; }
            set { _cachedAutoStart = value; try { EditorPrefs.SetBool("MCP_Native_AutoStart", value); } catch { } }
        }

        public static bool AutoRestart
        {
            get { EnsureCacheInitialized(); return _cachedAutoRestart; }
            set { _cachedAutoRestart = value; try { EditorPrefs.SetBool("MCP_Native_AutoRestart", value); } catch { } }
        }

        public static int MaxRestartAttempts
        {
            get { EnsureCacheInitialized(); return _cachedMaxRestartAttempts; }
            set { _cachedMaxRestartAttempts = value; try { EditorPrefs.SetInt("MCP_Native_MaxRestarts", value); } catch { } }
        }

        public static int RestartCooldownSeconds
        {
            get { EnsureCacheInitialized(); return _cachedRestartCooldownSeconds; }
            set { _cachedRestartCooldownSeconds = value; try { EditorPrefs.SetInt("MCP_Native_RestartCooldown", value); } catch { } }
        }

        // State
        public bool IsRunning => _httpServer?.IsRunning ?? false;
        public int ToolCount => _toolRegistry?.ToolCount ?? 0;
        public int ResourceCount => _resourceRegistry?.ResourceCount ?? 0;
        public TimeSpan Uptime => IsRunning ? DateTime.UtcNow - _startTime : TimeSpan.Zero;
        public int RestartCount => _restartCount;

        // Events
        public event Action OnServerStarted;
        public event Action OnServerStopped;
        public event Action<string, double> OnToolExecuted;
        public event Action<int> OnServerRestarted;

        // Persistent flag to track if server was running before domain reload
        private static bool WasRunningBeforeReload
        {
            get { EnsureCacheInitialized(); return _cachedWasRunningBeforeReload; }
            set { _cachedWasRunningBeforeReload = value; try { EditorPrefs.SetBool("MCP_Native_WasRunning", value); } catch { } }
        }

        static MCPNativeServer()
        {
            // Auto-initialize on domain reload
            EditorApplication.delayCall += () =>
            {
                // Check if server should start
                bool shouldStart = AutoStart || (AutoRestart && WasRunningBeforeReload);

                if (shouldStart)
                {
                    try
                    {
                        Debug.Log("[MCP Native JET] Starting server after domain reload...");
                        Instance.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[MCP Native JET] Auto-start failed: {ex.Message}");

                        // Try auto-restart if enabled
                        if (AutoRestart)
                        {
                            Instance.TryAutoRestart();
                        }
                    }
                }
            };

            // Register for quitting to clean up
            EditorApplication.quitting += () =>
            {
                WasRunningBeforeReload = false; // Don't auto-start after quit
            };
        }

        public MCPNativeServer()
        {
            Initialize();
        }

        #region Initialization

        private void Initialize()
        {
            if (_initialized) return;

            Port = EditorPrefs.GetInt("MCP_Port", 8080);

            // Create registries
            _toolRegistry = new MCPToolRegistry();
            _resourceRegistry = new MCPResourceRegistry();

            // Wire up events
            _toolRegistry.OnToolExecuted += (name, ms) =>
            {
                _totalToolCalls++;
                _totalResponseTime += ms;
                TrackResponseTime(ms);
                OnToolExecuted?.Invoke(name, ms);
            };

            // Discover existing tools
            _toolRegistry.DiscoverTools();

            // Register core tools
            RegisterCoreTools();

            // Register help/troubleshooting resource
            RegisterHelpResource();

            // Update custom-tools resource
            UpdateCustomToolsResource();

            _initialized = true;
            Debug.Log($"[MCP Native JET] Initialized with {_toolRegistry.ToolCount} tools and {_resourceRegistry.ResourceCount} resources");
        }

        private void RegisterCoreTools()
        {
            // Ping tool
            _toolRegistry.RegisterTool("ping", "Ping the server to test connection", (MCPSyncToolHandler)((args) =>
            {
                return new MCPToolResult
                {
                    Content = new List<MCPContent>
                    {
                        MCPContent.JsonContent(new
                        {
                            response = "pong",
                            server = SERVER_NAME,
                            version = VERSION,
                            timestamp = DateTime.UtcNow.ToString("o"),
                            uptime = Uptime.TotalSeconds,
                            status = "JET_MODE_ACTIVE"
                        })
                    }
                };
            }));

            // Server info tool
            _toolRegistry.RegisterTool("server_info", "Get MCP server information", (MCPSyncToolHandler)((args) =>
            {
                return new MCPToolResult
                {
                    Content = new List<MCPContent>
                    {
                        MCPContent.JsonContent(new
                        {
                            name = SERVER_NAME,
                            version = VERSION,
                            protocol = PROTOCOL_VERSION,
                            type = "Native C# JET (Zero Python)",
                            toolCount = _toolRegistry.ToolCount,
                            resourceCount = _resourceRegistry.ResourceCount,
                            uptime = Uptime.TotalSeconds,
                            unity = Application.unityVersion,
                            platform = Application.platform.ToString(),
                            autoRestart = AutoRestart,
                            restartCount = _restartCount,
                            metrics = GetPerformanceMetrics()
                        })
                    }
                };
            }));

            // Tool stats
            _toolRegistry.RegisterTool("tool_stats", "Get tool execution statistics", (MCPSyncToolHandler)((args) =>
            {
                return new MCPToolResult
                {
                    Content = new List<MCPContent>
                    {
                        MCPContent.JsonContent(_toolRegistry.GetStats())
                    }
                };
            }));

            // Server control tool
            _toolRegistry.RegisterTool("server_control", "Control MCP server. Actions: status, restart, set_auto_restart, get_metrics, help", (MCPSyncToolHandler)((args) =>
            {
                string action = args["action"]?.ToString()?.ToLower() ?? "status";

                switch (action)
                {
                    case "status":
                        return new MCPToolResult
                        {
                            Content = new List<MCPContent>
                            {
                                MCPContent.JsonContent(new
                                {
                                    running = IsRunning,
                                    uptime = Uptime.TotalSeconds,
                                    toolCount = ToolCount,
                                    resourceCount = ResourceCount,
                                    autoStart = AutoStart,
                                    autoRestart = AutoRestart,
                                    restartCount = _restartCount,
                                    port = Port,
                                    metrics = GetPerformanceMetrics()
                                })
                            }
                        };

                    case "restart":
                        EditorApplication.delayCall += Restart;
                        return new MCPToolResult
                        {
                            Content = new List<MCPContent> { MCPContent.TextContent("Server restart scheduled") }
                        };

                    case "set_auto_restart":
                        bool enable = args["enable"]?.ToObject<bool>() ?? true;
                        AutoRestart = enable;
                        return new MCPToolResult
                        {
                            Content = new List<MCPContent> { MCPContent.TextContent($"Auto-restart {(enable ? "enabled" : "disabled")}") }
                        };

                    case "get_metrics":
                        return new MCPToolResult
                        {
                            Content = new List<MCPContent> { MCPContent.JsonContent(GetPerformanceMetrics()) }
                        };

                    case "help":
                        return new MCPToolResult
                        {
                            Content = new List<MCPContent> { MCPContent.TextContent(GetHelpText()) }
                        };

                    default:
                        return new MCPToolResult
                        {
                            IsError = true,
                            Content = new List<MCPContent> { MCPContent.TextContent($"Unknown action: {action}") }
                        };
                }
            }));
        }

        private void RegisterHelpResource()
        {
            _resourceRegistry.RegisterResource("unity://mcp-help", "MCP Help", "Help and troubleshooting for MCP", (uri) =>
            {
                var help = new
                {
                    server = SERVER_NAME,
                    version = VERSION,
                    documentation = new
                    {
                        autoStart = "Server starts automatically when Unity loads. Enable via MCP Control Panel.",
                        autoRestart = "Server automatically restarts if it crashes. Max 5 attempts per minute.",
                        focusFree = "Server works even when Unity is not focused, thanks to background timers."
                    },
                    troubleshooting = new
                    {
                        unityAuthError = new
                        {
                            problem = "Unity account authentication error when connecting to MCP",
                            solutions = new[]
                            {
                                "1. Open Unity Hub and sign in to your Unity account",
                                "2. In Unity Editor: Edit > Preferences > Unity Services > Sign In",
                                "3. If offline: Edit > Preferences > General > Disable 'Send Editor Statistics'",
                                "4. Check firewall/proxy settings blocking Unity services",
                                "5. Try: Help > Unity Hub > Refresh License"
                            },
                            note = "MCP server itself doesn't require Unity authentication, but some Unity APIs might."
                        },
                        serverNotStarting = new
                        {
                            problem = "MCP server fails to start",
                            solutions = new[]
                            {
                                "1. Check if port 8080 is already in use (netstat -ano | findstr :8080)",
                                "2. Try a different port in MCP Control Panel",
                                "3. Check Windows Firewall settings",
                                "4. Restart Unity Editor"
                            }
                        },
                        toolsNotWorking = new
                        {
                            problem = "Tools not responding or timing out",
                            solutions = new[]
                            {
                                "1. Check Unity console for compilation errors",
                                "2. Use 'force_refresh' action to reload scripts",
                                "3. Check if Unity is in Play mode (some tools require Edit mode)",
                                "4. Restart MCP server via Control Panel"
                            }
                        }
                    },
                    settings = new
                    {
                        autoStart = AutoStart,
                        autoRestart = AutoRestart,
                        maxRestartAttempts = MaxRestartAttempts,
                        restartCooldownSeconds = RestartCooldownSeconds,
                        port = Port
                    }
                };

                return Task.FromResult(new MCPResourceContent
                {
                    Uri = uri,
                    MimeType = "application/json",
                    Text = JsonConvert.SerializeObject(help, Formatting.Indented)
                });
            });
        }

        private void UpdateCustomToolsResource()
        {
            _resourceRegistry.RegisterResource("unity://custom-tools", "Custom Tools", "List of registered custom tools", (uri) =>
            {
                var tools = _toolRegistry.GetAllTools();
                var data = new
                {
                    success = true,
                    message = "Custom tools retrieved successfully.",
                    data = new
                    {
                        project_id = PlayerSettings.productGUID.ToString().Substring(0, 16),
                        tool_count = tools.Count,
                        server_version = VERSION,
                        tools = tools.Select(t => new
                        {
                            name = t.Name,
                            description = t.Description,
                            structured_output = true,
                            requires_polling = false
                        }).ToList()
                    }
                };

                return Task.FromResult(new MCPResourceContent
                {
                    Uri = uri,
                    MimeType = "application/json",
                    Text = JsonConvert.SerializeObject(data, Formatting.Indented)
                });
            });
        }

        #endregion

        #region Server Control

        public void Start(int? port = null)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[MCP Native JET] Server is already running");
                return;
            }

            if (port.HasValue)
            {
                Port = port.Value;
            }

            try
            {
                _httpServer = new MCPHttpServer(Port);
                _httpServer.RequestHandler = HandleRequest;

                _httpServer.OnServerStarted += () =>
                {
                    OnServerStarted?.Invoke();
                    Debug.Log($"[MCP Native JET] Server started on port {Port} with {ToolCount} tools");
                };

                _httpServer.OnServerStopped += () =>
                {
                    OnServerStopped?.Invoke();

                    // Auto-restart logic
                    if (AutoRestart && !EditorApplication.isCompiling)
                    {
                        TryAutoRestart();
                    }
                };

                _httpServer.OnError += (path, ex) =>
                {
                    Debug.LogError($"[MCP Native JET] Error on {path}: {ex.Message}");
                };

                _httpServer.Start();
                _startTime = DateTime.UtcNow;

                // Mark as running for domain reload recovery
                WasRunningBeforeReload = true;

                // Start background timer for processing requests when Unity is not focused
                StartBackgroundTimer();

                // Start health check timer
                StartHealthCheckTimer();

                EditorPrefs.SetInt("MCP_Port", Port);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Native JET] Failed to start server: {ex.Message}");

                if (AutoRestart)
                {
                    TryAutoRestart();
                }
                else
                {
                    throw;
                }
            }
        }

        private void TryAutoRestart()
        {
            var timeSinceLastRestart = DateTime.UtcNow - _lastRestartTime;

            // Reset restart count if cooldown period has passed
            if (timeSinceLastRestart.TotalSeconds > RestartCooldownSeconds)
            {
                _restartCount = 0;
            }

            if (_restartCount >= MaxRestartAttempts)
            {
                Debug.LogError($"[MCP Native JET] Max restart attempts ({MaxRestartAttempts}) reached. Please restart manually.");
                return;
            }

            _restartCount++;
            _lastRestartTime = DateTime.UtcNow;

            Debug.Log($"[MCP Native JET] Auto-restarting... (attempt {_restartCount}/{MaxRestartAttempts})");

            EditorApplication.delayCall += () =>
            {
                try
                {
                    Start();
                    OnServerRestarted?.Invoke(_restartCount);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MCP Native JET] Auto-restart failed: {ex.Message}");
                }
            };
        }

        public void Stop()
        {
            // Mark as not running - manual stop shouldn't trigger auto-restart after reload
            WasRunningBeforeReload = false;

            StopHealthCheckTimer();
            StopBackgroundTimer();
            _httpServer?.Stop();
            _httpServer?.Dispose();
            _httpServer = null;
        }

        private void StartBackgroundTimer()
        {
            _backgroundTimer = new System.Timers.Timer(100); // 100ms interval
            _backgroundTimer.Elapsed += (s, e) =>
            {
                try
                {
                    MCPToolRegistry.RequestUpdate();
                }
                catch { }
            };
            _backgroundTimer.AutoReset = true;
            _backgroundTimer.Start();
        }

        private void StopBackgroundTimer()
        {
            _backgroundTimer?.Stop();
            _backgroundTimer?.Dispose();
            _backgroundTimer = null;
        }

        private void StartHealthCheckTimer()
        {
            _healthCheckTimer = new System.Timers.Timer(5000); // 5 second interval
            _healthCheckTimer.Elapsed += (s, e) =>
            {
                // Check if server is still responding
                if (_httpServer != null && !_httpServer.IsRunning && AutoRestart)
                {
                    Debug.LogWarning("[MCP Native JET] Server health check failed, triggering restart...");
                    EditorApplication.delayCall += () => TryAutoRestart();
                }
            };
            _healthCheckTimer.AutoReset = true;
            _healthCheckTimer.Start();
        }

        private void StopHealthCheckTimer()
        {
            _healthCheckTimer?.Stop();
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;
        }

        public void Restart()
        {
            Stop();
            EditorApplication.delayCall += () => Start();
        }

        public void KillServerProcess()
        {
            // Forcefully stop the server
            // In the future this could find the process ID and kill it externally if needed
            Stop();
        }

        #endregion

        #region Performance Metrics

        private void TrackResponseTime(double ms)
        {
            _recentResponseTimes.Enqueue(ms);
            while (_recentResponseTimes.Count > MaxRecentResponses)
            {
                _recentResponseTimes.TryDequeue(out _);
            }
        }

        public object GetPerformanceMetrics()
        {
            var recentTimes = _recentResponseTimes.ToArray();
            double avgTime = recentTimes.Length > 0 ? recentTimes.Average() : 0;
            double maxTime = recentTimes.Length > 0 ? recentTimes.Max() : 0;
            double minTime = recentTimes.Length > 0 ? recentTimes.Min() : 0;

            return new
            {
                totalRequests = _totalRequests,
                totalToolCalls = _totalToolCalls,
                averageResponseTimeMs = Math.Round(avgTime, 2),
                maxResponseTimeMs = Math.Round(maxTime, 2),
                minResponseTimeMs = Math.Round(minTime, 2),
                uptime = Uptime.TotalSeconds,
                requestsPerSecond = Uptime.TotalSeconds > 0 ? Math.Round(_totalRequests / Uptime.TotalSeconds, 2) : 0,
                restartCount = _restartCount
            };
        }

        private string GetHelpText()
        {
            return @"
=== MCP Native JET Server Help ===

FEATURES:
- Auto-Start: Server starts when Unity loads
- Auto-Restart: Server restarts automatically if it crashes
- Focus-Free: Works even when Unity is not focused
- High Performance: Pure C# implementation

TROUBLESHOOTING:

1. Unity Account Authentication Error:
   - Sign in to Unity Hub
   - Edit > Preferences > Unity Services > Sign In
   - This is a Unity issue, not MCP

2. Server Not Starting:
   - Check if port 8080 is in use
   - Try different port in Control Panel
   - Check firewall settings

3. Tools Not Responding:
   - Check console for errors
   - Use auto_compile tool to refresh
   - Restart server

ACTIONS:
- status: Get server status
- restart: Restart the server
- set_auto_restart: Enable/disable auto-restart
- get_metrics: Get performance metrics
- help: Show this help

For more info, read resource: unity://mcp-help
";
        }

        #endregion

        #region Request Handling

        private async Task<JsonRpcResponse> HandleRequest(JsonRpcRequest request)
        {
            var startTime = DateTime.UtcNow;
            _totalRequests++;

            // Try to wake up Unity for main thread processing
            MCPToolRegistry.RequestUpdate();

            try
            {
                string method = request.Method?.ToLower() ?? "";

                JsonRpcResponse response = method switch
                {
                    "initialize" => HandleInitialize(request),
                    "initialized" => JsonRpcResponse.Success(request.Id, new { }),
                    "tools/list" => HandleToolsList(request),
                    "tools/call" => await HandleToolsCall(request),
                    "resources/list" => HandleResourcesList(request),
                    "resources/read" => await HandleResourcesRead(request),
                    "ping" => JsonRpcResponse.Success(request.Id, new { response = "pong", server = SERVER_NAME }),
                    "shutdown" => HandleShutdown(request),
                    _ => await HandleDirectToolCall(request, method)
                };

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Native JET] Request handling error: {ex.Message}");
                return JsonRpcResponse.Failure(request.Id, JsonRpcError.InternalError, ex.Message);
            }
        }

        private async Task<JsonRpcResponse> HandleDirectToolCall(JsonRpcRequest request, string method)
        {
            if (_toolRegistry.HasTool(method))
            {
                var args = request.Params as JObject ?? new JObject();
                var result = await _toolRegistry.ExecuteTool(method, args);
                return JsonRpcResponse.Success(request.Id, result);
            }

            return JsonRpcResponse.Failure(request.Id, JsonRpcError.MethodNotFound, $"Method not found: {method}");
        }

        private JsonRpcResponse HandleShutdown(JsonRpcRequest request)
        {
            EditorApplication.delayCall += Stop;
            return JsonRpcResponse.Success(request.Id, new { message = "Shutting down" });
        }

        private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
        {
            var result = new MCPInitializeResult
            {
                ProtocolVersion = PROTOCOL_VERSION,
                ServerInfo = new MCPServerInfo
                {
                    Name = SERVER_NAME,
                    Version = VERSION
                },
                Capabilities = new MCPCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = true },
                    Resources = new ResourcesCapability { Subscribe = false, ListChanged = true }
                },
                Instructions = $"Unity MCP Vibe Native JET Server - {_toolRegistry.ToolCount} tools available. Auto-restart: {AutoRestart}. Use tools/list to see all available tools. Read unity://mcp-help for troubleshooting."
            };

            return JsonRpcResponse.Success(request.Id, result);
        }

        private JsonRpcResponse HandleToolsList(JsonRpcRequest request)
        {
            var result = new MCPToolsListResult
            {
                Tools = _toolRegistry.GetAllTools()
            };

            return JsonRpcResponse.Success(request.Id, result);
        }

        private async Task<JsonRpcResponse> HandleToolsCall(JsonRpcRequest request)
        {
            var callParams = request.Params?.ToObject<MCPToolCallParams>();
            if (callParams == null || string.IsNullOrEmpty(callParams.Name))
            {
                return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams, "Missing tool name");
            }

            var result = await _toolRegistry.ExecuteTool(callParams.Name, callParams.Arguments ?? new JObject());
            return JsonRpcResponse.Success(request.Id, result);
        }

        private JsonRpcResponse HandleResourcesList(JsonRpcRequest request)
        {
            var result = new MCPResourcesListResult
            {
                Resources = _resourceRegistry.GetAllResources()
            };

            return JsonRpcResponse.Success(request.Id, result);
        }

        private async Task<JsonRpcResponse> HandleResourcesRead(JsonRpcRequest request)
        {
            var uri = request.Params?["uri"]?.ToString();
            if (string.IsNullOrEmpty(uri))
            {
                return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams, "Missing resource URI");
            }

            var result = await _resourceRegistry.ReadResource(uri);
            return JsonRpcResponse.Success(request.Id, result);
        }

        #endregion

        #region Tool Registration API

        /// <summary>
        /// Register a custom tool
        /// </summary>
        public void RegisterTool(string name, string description, Func<JObject, object> handler)
        {
            _toolRegistry.RegisterTool(name, description, handler);
            UpdateCustomToolsResource();
        }

        /// <summary>
        /// Register a custom resource
        /// </summary>
        public void RegisterResource(string uri, string name, string description, Func<string, MCPResourceContent> handler)
        {
            _resourceRegistry.RegisterResource(uri, name, description, (u) => handler(u));
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            Stop();
            _instance = null;
        }

        #endregion
    }
}
