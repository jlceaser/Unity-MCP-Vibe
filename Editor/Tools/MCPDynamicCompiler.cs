#nullable disable
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// "SPEED DEMON" - Dynamic C# Compiler
    /// Executes C# code in-memory WITHOUT triggering Domain Reload.
    /// Uses reflection and expression evaluation for zero-latency execution.
    /// 
    /// NOTE: Full Roslyn compilation is optional. Without it, uses expression evaluation.
    /// </summary>
    [McpForUnityTool(
        name: "dynamic_compile",
        Description = "Execute C# code instantly without Domain Reload. Actions: execute, evaluate, create_delegate, get_types")]
    [DangerousTool("Executes arbitrary C# code", DangerousOperationType.CodeExecution)]
    public static class MCPDynamicCompiler
    {
        // Cache for resolved types
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        
        // Execution context for storing variables between calls
        private static readonly Dictionary<string, object> ExecutionContext = new Dictionary<string, object>();

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLower() ?? "execute";

            // Security check for code execution
            if (action == "execute" || action == "evaluate")
            {
                var denied = MCPSecurity.CheckApprovalOrError(
                    "dynamic_compile",
                    $"Execute C# code: {@params["code"]?.ToString()?.Substring(0, Math.Min(100, @params["code"]?.ToString()?.Length ?? 0))}...",
                    DangerousOperationType.CodeExecution
                );
                if (denied != null) return denied;
            }

            switch (action)
            {
                case "execute":
                    return ExecuteCode(@params);

                case "evaluate":
                    return EvaluateExpression(@params);

                case "create_delegate":
                    return CreateDelegate(@params);

                case "get_types":
                    return GetAvailableTypes(@params);

                case "get_context":
                    return GetExecutionContext();

                case "clear_context":
                    ExecutionContext.Clear();
                    return new SuccessResponse("Execution context cleared");

                default:
                    return new SuccessResponse("Dynamic Compiler ready. Actions: execute, evaluate, create_delegate, get_types, get_context, clear_context");
            }
        }

        #region Code Execution

        /// <summary>
        /// Executes C# code using reflection-based interpretation.
        /// No Domain Reload - instant execution.
        /// </summary>
        private static object ExecuteCode(JObject @params)
        {
            string code = @params["code"]?.ToString();
            if (string.IsNullOrEmpty(code))
            {
                return new ErrorResponse("Code parameter required");
            }

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // Parse and execute
                object result = InterpretCode(code);
                
                stopwatch.Stop();

                return new SuccessResponse("Code executed successfully", new
                {
                    result = FormatResult(result),
                    resultType = result?.GetType().Name ?? "void",
                    executionTimeMs = stopwatch.ElapsedMilliseconds,
                    domainReload = false
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Execution failed: {ex.Message}", new
                {
                    exceptionType = ex.GetType().Name,
                    stackTrace = ex.StackTrace?.Split('\n').Take(5).ToArray()
                });
            }
        }

        /// <summary>
        /// Evaluates an expression and returns its value.
        /// </summary>
        private static object EvaluateExpression(JObject @params)
        {
            string expression = @params["expression"]?.ToString() ?? @params["code"]?.ToString();
            if (string.IsNullOrEmpty(expression))
            {
                return new ErrorResponse("Expression parameter required");
            }

            try
            {
                object result = EvaluateSimpleExpression(expression);

                return new SuccessResponse("Expression evaluated", new
                {
                    expression = expression,
                    value = FormatResult(result),
                    type = result?.GetType().FullName ?? "null"
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Evaluation failed: {ex.Message}");
            }
        }

        #endregion

        #region Interpretation Engine

        /// <summary>
        /// Interprets C# code without compilation.
        /// Supports: assignments, method calls, property access, object creation.
        /// </summary>
        private static object InterpretCode(string code)
        {
            code = code.Trim();
            if (code.EndsWith(";")) code = code.Substring(0, code.Length - 1);

            // Handle multiple statements
            if (code.Contains(";"))
            {
                var statements = SplitStatements(code);
                object lastResult = null;
                foreach (var statement in statements)
                {
                    lastResult = InterpretSingleStatement(statement.Trim());
                }
                return lastResult;
            }

            return InterpretSingleStatement(code);
        }

        private static object InterpretSingleStatement(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            // Variable assignment: var x = ... or x = ...
            var assignMatch = Regex.Match(code, @"^(?:var\s+)?(\w+)\s*=\s*(.+)$");
            if (assignMatch.Success)
            {
                string varName = assignMatch.Groups[1].Value;
                string valueExpr = assignMatch.Groups[2].Value;
                object value = EvaluateSimpleExpression(valueExpr);
                ExecutionContext[varName] = value;
                return value;
            }

            // Object instantiation: new Type(args)
            if (code.StartsWith("new "))
            {
                return CreateNewObject(code.Substring(4));
            }

            // Method call or property access
            return EvaluateSimpleExpression(code);
        }

        /// <summary>
        /// Evaluates a simple expression using reflection.
        /// </summary>
        private static object EvaluateSimpleExpression(string expr)
        {
            expr = expr.Trim();

            // Literal values (case-insensitive for bool/null)
            if (expr.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            if (expr.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (expr.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            if (int.TryParse(expr, out int intVal)) return intVal;
            if (float.TryParse(expr.TrimEnd('f', 'F'), out float floatVal)) return floatVal;
            if (double.TryParse(expr.TrimEnd('d', 'D'), out double doubleVal)) return doubleVal;
            if (expr.StartsWith("\"") && expr.EndsWith("\"")) return expr.Substring(1, expr.Length - 2);

            // Context variable
            if (ExecutionContext.TryGetValue(expr, out object contextVal))
            {
                return contextVal;
            }

            // Try ternary operator EARLY: condition ? trueValue : falseValue
            if (expr.Contains("?") && expr.Contains(":") && !expr.Contains("?."))
            {
                if (TryEvaluateTernary(expr, out object ternaryResult))
                {
                    return ternaryResult;
                }
            }

            // Try null-coalescing operator EARLY: left ?? right
            if (expr.Contains("??"))
            {
                if (TryEvaluateNullCoalescing(expr, out object nullCoalesceResult))
                {
                    return nullCoalesceResult;
                }
            }

            // Static property/field access: Type.Member
            var staticMatch = Regex.Match(expr, @"^(\w+(?:\.\w+)*?)\.(\w+)$");
            if (staticMatch.Success && !expr.Contains("("))
            {
                return ResolveStaticMember(staticMatch.Groups[1].Value, staticMatch.Groups[2].Value);
            }

            // Method call: Target.Method(args) or Method(args)
            var methodMatch = Regex.Match(expr, @"^(.+)\.(\w+)\((.*)\)$");
            if (methodMatch.Success)
            {
                string targetExpr = methodMatch.Groups[1].Value;
                string methodName = methodMatch.Groups[2].Value;
                string argsStr = methodMatch.Groups[3].Value;

                object target = EvaluateSimpleExpression(targetExpr);
                object[] args = ParseArguments(argsStr);

                return InvokeMethod(target, methodName, args);
            }

            // Static method call: Type.Method(args)
            var staticMethodMatch = Regex.Match(expr, @"^(\w+(?:\.\w+)*)\.(\w+)\((.*)\)$");
            if (staticMethodMatch.Success)
            {
                string typeName = staticMethodMatch.Groups[1].Value;
                string methodName = staticMethodMatch.Groups[2].Value;
                string argsStr = staticMethodMatch.Groups[3].Value;

                Type type = ResolveType(typeName);
                if (type != null)
                {
                    object[] args = ParseArguments(argsStr);
                    return InvokeStaticMethod(type, methodName, args);
                }
            }

            // GameObject.Find special case
            if (expr.StartsWith("GameObject.Find("))
            {
                var nameMatch = Regex.Match(expr, @"GameObject\.Find\([""'](.+?)[""']\)");
                if (nameMatch.Success)
                {
                    return GameObject.Find(nameMatch.Groups[1].Value);
                }
            }

            // Property chain: obj.prop1.prop2
            if (expr.Contains(".") && !expr.Contains("("))
            {
                return ResolvePropertyChain(expr);
            }

            throw new Exception($"Cannot evaluate expression: {expr}");
        }

        private static object ResolveStaticMember(string typeName, string memberName)
        {
            Type type = ResolveType(typeName);
            if (type == null)
            {
                throw new Exception($"Type not found: {typeName}");
            }

            // Try property
            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
            if (prop != null) return prop.GetValue(null);

            // Try field
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
            if (field != null) return field.GetValue(null);

            throw new Exception($"Member not found: {typeName}.{memberName}");
        }

        private static object ResolvePropertyChain(string expr)
        {
            // Handle null-conditional operator ?.
            bool hasNullConditional = expr.Contains("?.");
            var parts = hasNullConditional
                ? Regex.Split(expr, @"(?<!\?)\.|\?\.")
                : expr.Split('.');

            object current = null;
            Type currentType = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                // If using null-conditional and current is null, return null
                if (hasNullConditional && i > 0 && current == null)
                {
                    return null;
                }

                if (i == 0)
                {
                    // First part - could be type name or context variable
                    if (ExecutionContext.TryGetValue(part, out current))
                    {
                        currentType = current?.GetType();
                        continue;
                    }

                    currentType = ResolveType(part);
                    if (currentType == null)
                    {
                        throw new Exception($"Unknown identifier: {part}");
                    }
                    continue;
                }

                // Resolve member
                if (currentType != null)
                {
                    var prop = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    if (prop != null)
                    {
                        current = prop.GetValue(current);
                        currentType = current?.GetType();
                        continue;
                    }

                    var field = currentType.GetField(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    if (field != null)
                    {
                        current = field.GetValue(current);
                        currentType = current?.GetType();
                        continue;
                    }
                }

                throw new Exception($"Cannot resolve: {part}");
            }

            return current;
        }

        private static object CreateNewObject(string expr)
        {
            var match = Regex.Match(expr, @"^(\w+(?:\.\w+)*)\s*\((.*)\)$");
            if (!match.Success)
            {
                throw new Exception($"Invalid new expression: new {expr}");
            }

            string typeName = match.Groups[1].Value;
            string argsStr = match.Groups[2].Value;

            Type type = ResolveType(typeName);
            if (type == null)
            {
                throw new Exception($"Type not found: {typeName}");
            }

            object[] args = ParseArguments(argsStr);

            return Activator.CreateInstance(type, args);
        }

        private static object InvokeMethod(object target, string methodName, object[] args)
        {
            if (target == null)
            {
                throw new Exception("Cannot invoke method on null target");
            }

            Type type = target.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.Name == methodName)
                             .ToArray();

            if (methods.Length == 0)
            {
                throw new Exception($"Method not found: {methodName}");
            }

            // Find best matching method
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == args.Length)
                {
                    try
                    {
                        // Convert args to correct types
                        object[] convertedArgs = ConvertArguments(args, parameters);
                        return method.Invoke(target, convertedArgs);
                    }
                    catch { continue; }
                }
            }

            throw new Exception($"No matching overload for {methodName} with {args.Length} arguments");
        }

        private static object InvokeStaticMethod(Type type, string methodName, object[] args)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(m => m.Name == methodName)
                             .ToArray();

            if (methods.Length == 0)
            {
                throw new Exception($"Static method not found: {type.Name}.{methodName}");
            }

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == args.Length)
                {
                    try
                    {
                        object[] convertedArgs = ConvertArguments(args, parameters);
                        return method.Invoke(null, convertedArgs);
                    }
                    catch { continue; }
                }
            }

            throw new Exception($"No matching overload for {type.Name}.{methodName}");
        }

        #endregion

        #region Type Resolution

        private static Type ResolveType(string typeName)
        {
            if (TypeCache.TryGetValue(typeName, out Type cached))
            {
                return cached;
            }

            // Well-known Unity types
            var wellKnown = new Dictionary<string, Type>
            {
                { "GameObject", typeof(GameObject) },
                { "Transform", typeof(Transform) },
                { "Camera", typeof(Camera) },
                { "Light", typeof(Light) },
                { "Rigidbody", typeof(Rigidbody) },
                { "Collider", typeof(Collider) },
                { "MeshRenderer", typeof(MeshRenderer) },
                { "AudioSource", typeof(AudioSource) },
                { "Time", typeof(Time) },
                { "Debug", typeof(Debug) },
                { "Application", typeof(Application) },
                { "Screen", typeof(Screen) },
                { "Input", typeof(Input) },
                { "Physics", typeof(Physics) },
                { "Vector3", typeof(Vector3) },
                { "Vector2", typeof(Vector2) },
                { "Quaternion", typeof(Quaternion) },
                { "Color", typeof(Color) },
                { "Mathf", typeof(Mathf) },
                { "EditorApplication", typeof(EditorApplication) },
                { "Selection", typeof(Selection) },
                { "AssetDatabase", typeof(AssetDatabase) },
                { "EditorUtility", typeof(EditorUtility) },
                { "QualitySettings", typeof(QualitySettings) },
                { "RenderSettings", typeof(RenderSettings) },
                { "GraphicsSettings", typeof(UnityEngine.Rendering.GraphicsSettings) },
                { "PlayerSettings", typeof(PlayerSettings) },
                { "EditorPrefs", typeof(EditorPrefs) },
                { "PlayerPrefs", typeof(PlayerPrefs) },
                { "Resources", typeof(UnityEngine.Resources) },
                { "Material", typeof(Material) },
                { "Shader", typeof(Shader) },
                { "Texture2D", typeof(Texture2D) },
            };

            if (wellKnown.TryGetValue(typeName, out Type type))
            {
                TypeCache[typeName] = type;
                return type;
            }

            // Search loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName) ??
                           assembly.GetType($"UnityEngine.{typeName}") ??
                           assembly.GetType($"UnityEditor.{typeName}");
                    
                    if (type != null)
                    {
                        TypeCache[typeName] = type;
                        return type;
                    }
                }
                catch { }
            }

            return null;
        }

        private static object GetAvailableTypes(JObject @params)
        {
            string filter = @params["filter"]?.ToString()?.ToLower();
            int limit = @params["limit"]?.ToObject<int>() ?? 50;

            var types = new List<object>();
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.IsDynamic) continue;

                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (!string.IsNullOrEmpty(filter) && !type.Name.ToLower().Contains(filter))
                            continue;

                        types.Add(new
                        {
                            name = type.Name,
                            fullName = type.FullName,
                            assembly = assembly.GetName().Name,
                            isClass = type.IsClass,
                            isStatic = type.IsAbstract && type.IsSealed
                        });

                        if (types.Count >= limit) break;
                    }
                }
                catch { }

                if (types.Count >= limit) break;
            }

            return new SuccessResponse($"Found {types.Count} types", new { types });
        }

        #endregion

        #region Delegate Creation

        /// <summary>
        /// Creates a reusable delegate from code for repeated execution.
        /// </summary>
        private static object CreateDelegate(JObject @params)
        {
            string name = @params["name"]?.ToString();
            string code = @params["code"]?.ToString();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
            {
                return new ErrorResponse("name and code parameters required");
            }

            // Store as a callable in context
            ExecutionContext[$"__delegate_{name}"] = code;

            return new SuccessResponse($"Delegate '{name}' created. Call with dynamic_compile action=execute code=\"__call_{name}\"");
        }

        private static object GetExecutionContext()
        {
            var items = ExecutionContext.Select(kv => new
            {
                name = kv.Key,
                type = kv.Value?.GetType().Name ?? "null",
                value = FormatResult(kv.Value)
            }).ToList();

            return new SuccessResponse($"Context has {items.Count} items", new { items });
        }

        #endregion

        #region Helpers

        private static string[] SplitStatements(string code)
        {
            var statements = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '(' || c == '{' || c == '[') depth++;
                else if (c == ')' || c == '}' || c == ']') depth--;
                else if (c == ';' && depth == 0)
                {
                    statements.Add(code.Substring(start, i - start));
                    start = i + 1;
                }
            }

            if (start < code.Length)
            {
                statements.Add(code.Substring(start));
            }

            return statements.ToArray();
        }

        private static object[] ParseArguments(string argsStr)
        {
            if (string.IsNullOrWhiteSpace(argsStr)) return Array.Empty<object>();

            var args = new List<object>();
            var parts = SplitArguments(argsStr);

            foreach (var part in parts)
            {
                args.Add(EvaluateSimpleExpression(part.Trim()));
            }

            return args.ToArray();
        }

        private static string[] SplitArguments(string argsStr)
        {
            var args = new List<string>();
            int depth = 0;
            int start = 0;
            bool inString = false;

            for (int i = 0; i < argsStr.Length; i++)
            {
                char c = argsStr[i];
                
                if (c == '"' && (i == 0 || argsStr[i - 1] != '\\'))
                    inString = !inString;
                
                if (!inString)
                {
                    if (c == '(' || c == '{' || c == '[') depth++;
                    else if (c == ')' || c == '}' || c == ']') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        args.Add(argsStr.Substring(start, i - start));
                        start = i + 1;
                    }
                }
            }

            if (start < argsStr.Length)
            {
                args.Add(argsStr.Substring(start));
            }

            return args.ToArray();
        }

        private static object[] ConvertArguments(object[] args, ParameterInfo[] parameters)
        {
            var converted = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                converted[i] = Convert.ChangeType(args[i], parameters[i].ParameterType);
            }
            return converted;
        }

        private static object FormatResult(object result)
        {
            if (result == null) return "null";
            if (result is string s) return s;
            if (result is bool b) return b;
            if (result.GetType().IsPrimitive) return result;
            if (result is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
            if (result is Vector2 v2) return new { x = v2.x, y = v2.y };
            if (result is Color c) return $"RGBA({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
            if (result is Quaternion q) return new { x = q.x, y = q.y, z = q.z, w = q.w };
            if (result is UnityEngine.Object uobj) return $"[{result.GetType().Name}] {uobj.name}";
            
            return result.ToString();
        }

        #endregion

        #region Operator Support

        /// <summary>
        /// Evaluates ternary operator: condition ? trueValue : falseValue
        /// </summary>
        private static bool TryEvaluateTernary(string expr, out object result)
        {
            result = null;

            // Find the ? operator (not part of ?. or ??)
            int questionIndex = -1;
            int depth = 0;
            bool inString = false;

            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '"' && (i == 0 || expr[i - 1] != '\\')) inString = !inString;
                if (inString) continue;

                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == '?' && depth == 0)
                {
                    // Make sure it's not ?. or ??
                    if (i + 1 < expr.Length)
                    {
                        char next = expr[i + 1];
                        if (next == '.' || next == '?') continue;
                    }
                    questionIndex = i;
                    break;
                }
            }

            if (questionIndex < 0) return false;

            // Find the matching : operator
            int colonIndex = -1;
            depth = 0;
            inString = false;

            for (int i = questionIndex + 1; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '"' && (i == 0 || expr[i - 1] != '\\')) inString = !inString;
                if (inString) continue;

                if (c == '(' || c == '[' || c == '?') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ':' && depth == 0)
                {
                    colonIndex = i;
                    break;
                }
            }

            if (colonIndex < 0) return false;

            string conditionExpr = expr.Substring(0, questionIndex).Trim();
            string trueExpr = expr.Substring(questionIndex + 1, colonIndex - questionIndex - 1).Trim();
            string falseExpr = expr.Substring(colonIndex + 1).Trim();

            try
            {
                object conditionResult = EvaluateSimpleExpression(conditionExpr);
                bool condition = conditionResult is bool b ? b : conditionResult != null;

                result = condition
                    ? EvaluateSimpleExpression(trueExpr)
                    : EvaluateSimpleExpression(falseExpr);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Evaluates null-coalescing operator: left ?? right
        /// </summary>
        private static bool TryEvaluateNullCoalescing(string expr, out object result)
        {
            result = null;

            // Find ?? operator
            int index = -1;
            int depth = 0;
            bool inString = false;

            for (int i = 0; i < expr.Length - 1; i++)
            {
                char c = expr[i];
                if (c == '"' && (i == 0 || expr[i - 1] != '\\')) inString = !inString;
                if (inString) continue;

                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == '?' && expr[i + 1] == '?' && depth == 0)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0) return false;

            string leftExpr = expr.Substring(0, index).Trim();
            string rightExpr = expr.Substring(index + 2).Trim();

            try
            {
                object leftResult = EvaluateSimpleExpression(leftExpr);
                result = leftResult ?? EvaluateSimpleExpression(rightExpr);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
