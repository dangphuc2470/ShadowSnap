using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShadowSnap;

internal static class McpServer
{
    public static void Run()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);

        if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
        {
            NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        }

        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null) break; // Client closed stdin stream

            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("method", out JsonElement methodProp))
                {
                    continue;
                }

                string method = methodProp.GetString() ?? "";
                bool hasId = root.TryGetProperty("id", out JsonElement idProp);

                // Notifications do not have responses
                if (method.StartsWith("notifications/") || !hasId)
                {
                    continue;
                }

                HandleRequest(method, idProp, root);
            }
            catch (Exception ex)
            {
                SendError(null, -32700, $"Parse error / Internal exception: {ex.Message}");
            }
        }
    }

    private static void HandleRequest(string method, JsonElement idProp, JsonElement root)
    {
        root.TryGetProperty("params", out JsonElement paramsProp);

        switch (method)
        {
            case "initialize":
                SendResult(idProp, new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject()
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "shadowsnap",
                        ["version"] = "1.0.0"
                    }
                });
                break;

            case "ping":
                SendResult(idProp, new JsonObject());
                break;

            case "tools/list":
                SendResult(idProp, new JsonObject
                {
                    ["tools"] = GetToolsList()
                });
                break;

            case "tools/call":
                HandleToolsCall(idProp, paramsProp);
                break;

            case "prompts/list":
                SendResult(idProp, new JsonObject { ["prompts"] = new JsonArray() });
                break;

            case "resources/list":
                SendResult(idProp, new JsonObject { ["resources"] = new JsonArray() });
                break;

            default:
                SendError(idProp, -32601, $"Method '{method}' not found");
                break;
        }
    }

    private static JsonArray GetToolsList()
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["name"] = "list_windows",
                ["description"] = "Lists all visible top-level desktop windows with their HWND handle, window title, process name, process ID, bounds, and minimized state.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["filter"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional case-insensitive substring filter for window title or process name."
                        },
                        ["include_empty_titles"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to include windows with empty titles. Default is false."
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"] = "capture_window",
                ["description"] = "Captures a high-quality window screenshot with authentic macOS-style drop shadow and transparent PNG background.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["window_handle"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "The window handle (HWND) to capture. If omitted, title_filter or process_name is used."
                        },
                        ["title_filter"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Case-insensitive substring to find the target window by its title."
                        },
                        ["process_name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Case-insensitive process name to match the target window (e.g. 'code', 'chrome', 'notepad')."
                        },
                        ["shadow_offset"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Drop shadow spread padding in pixels. Default is 32."
                        },
                        ["output_path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional destination file path for the PNG image. Defaults to Pictures/Screenshots/."
                        },
                        ["copy_to_clipboard"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to copy the captured image to the Windows clipboard. Default is false."
                        },
                        ["return_base64"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to return the base64-encoded image data in the response. Default is false."
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"] = "capture_active_window",
                ["description"] = "Captures the currently active foreground window with authentic drop shadow.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["shadow_offset"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Drop shadow spread padding in pixels. Default is 32."
                        },
                        ["output_path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional destination file path for the PNG image."
                        },
                        ["copy_to_clipboard"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to copy the captured image to the Windows clipboard. Default is false."
                        },
                        ["return_base64"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to return the base64-encoded image data in the response. Default is false."
                        }
                    }
                }
            }
        };
    }

    private static void HandleToolsCall(JsonElement idProp, JsonElement paramsProp)
    {
        if (!paramsProp.TryGetProperty("name", out JsonElement nameProp))
        {
            SendError(idProp, -32602, "Missing tool name in params");
            return;
        }

        string toolName = nameProp.GetString() ?? "";
        paramsProp.TryGetProperty("arguments", out JsonElement argsProp);

        switch (toolName)
        {
            case "list_windows":
                ExecuteListWindows(idProp, argsProp);
                break;

            case "capture_window":
                ExecuteCaptureWindow(idProp, argsProp, isForegroundOnly: false);
                break;

            case "capture_active_window":
                ExecuteCaptureWindow(idProp, argsProp, isForegroundOnly: true);
                break;

            default:
                SendToolError(idProp, $"Unknown tool: {toolName}");
                break;
        }
    }

    private static void ExecuteListWindows(JsonElement idProp, JsonElement argsProp)
    {
        string? filter = null;
        bool includeEmptyTitles = false;

        if (argsProp.ValueKind == JsonValueKind.Object)
        {
            if (argsProp.TryGetProperty("filter", out JsonElement f)) filter = f.GetString();
            if (argsProp.TryGetProperty("include_empty_titles", out JsonElement e)) includeEmptyTitles = e.GetBoolean();
        }

        var windows = RunOnDefaultDesktop(() => EnumerateVisibleWindows(filter, includeEmptyTitles));
        string json = JsonSerializer.Serialize(windows, new JsonSerializerOptions { WriteIndented = true });

        SendToolSuccess(idProp, json);
    }

    private static void ExecuteCaptureWindow(JsonElement idProp, JsonElement argsProp, bool isForegroundOnly)
    {
        RunOnDefaultDesktop(() =>
        {
            nint targetHwnd = nint.Zero;
            string? titleFilter = null;
            string? processName = null;
            int shadowOffset = 32;
            string? outputPath = null;
            bool copyToClipboard = false;
            bool returnBase64 = false;

            if (argsProp.ValueKind == JsonValueKind.Object)
            {
                if (argsProp.TryGetProperty("window_handle", out JsonElement wh)) targetHwnd = (nint)wh.GetInt64();
                if (argsProp.TryGetProperty("title_filter", out JsonElement tf)) titleFilter = tf.GetString();
                if (argsProp.TryGetProperty("process_name", out JsonElement pn)) processName = pn.GetString();
                if (argsProp.TryGetProperty("shadow_offset", out JsonElement so)) shadowOffset = so.GetInt32();
                if (argsProp.TryGetProperty("output_path", out JsonElement op)) outputPath = op.GetString();
                if (argsProp.TryGetProperty("copy_to_clipboard", out JsonElement cc)) copyToClipboard = cc.GetBoolean();
                if (argsProp.TryGetProperty("return_base64", out JsonElement rb)) returnBase64 = rb.GetBoolean();
            }

            if (isForegroundOnly)
            {
                targetHwnd = NativeMethods.GetForegroundWindow();
            }
            else if (targetHwnd != nint.Zero)
            {
                if (!NativeMethods.IsWindow(targetHwnd))
                {
                    SendToolError(idProp, $"Window handle 0x{targetHwnd:X} is invalid or no longer exists.");
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(titleFilter) || !string.IsNullOrEmpty(processName))
            {
                var windows = EnumerateVisibleWindows(titleFilter ?? processName, includeEmptyTitles: false);
                var match = windows.FirstOrDefault(w =>
                    (!string.IsNullOrEmpty(titleFilter) && w.Title.Contains(titleFilter, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(processName) && w.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase)));

                if (match == null)
                {
                    SendToolError(idProp, $"No window matching filter '{titleFilter ?? processName}' was found.");
                    return;
                }
                targetHwnd = (nint)match.Handle;
            }
            else
            {
                targetHwnd = NativeMethods.GetForegroundWindow();
            }

            if (targetHwnd == nint.Zero || !NativeMethods.IsWindow(targetHwnd))
            {
                SendToolError(idProp, "Could not determine target window to capture.");
                return;
            }

            StringBuilder titleSb = new(512);
            NativeMethods.GetWindowText(targetHwnd, titleSb, 512);
            string windowTitle = titleSb.ToString().Trim();
            if (string.IsNullOrEmpty(windowTitle)) windowTitle = "Window";

            // Determine destination file path
            string finalPath = outputPath ?? "";
            if (string.IsNullOrWhiteSpace(finalPath))
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string screenshotsDir = Path.Combine(pictures, "Screenshots");
                Directory.CreateDirectory(screenshotsDir);

                string cleanTitle = string.Concat(windowTitle.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrEmpty(cleanTitle)) cleanTitle = "Window";
                if (cleanTitle.Length > 30) cleanTitle = cleanTitle[..30];

                finalPath = Path.Combine(screenshotsDir, $"Snap_{DateTime.Now:yyyyMMdd_HHmmss}_{cleanTitle}.png");
            }
            else
            {
                string? parentDir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }
            }

            try
            {
                using Bitmap? captured = WindowCaptureEngine.CaptureWindow(targetHwnd, shadowOffset);
                if (captured == null)
                {
                    SendToolError(idProp, "Failed to capture window (window bounds empty or capture returned null).");
                    return;
                }

                captured.Save(finalPath, ImageFormat.Png);

                if (copyToClipboard)
                {
                    ClipboardHelper.SetClipboardImage(captured);
                }

                JsonArray contentArray = new();
                string textMsg = $"Captured window '{windowTitle}' (HWND: 0x{targetHwnd:X}, Size: {captured.Width}x{captured.Height}px)\nSaved to: {finalPath}";
                contentArray.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = textMsg
                });

                if (returnBase64)
                {
                    using MemoryStream ms = new();
                    captured.Save(ms, ImageFormat.Png);
                    string b64 = Convert.ToBase64String(ms.ToArray());
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["data"] = b64,
                        ["mimeType"] = "image/png"
                    });
                }

                SendResult(idProp, new JsonObject
                {
                    ["content"] = contentArray,
                    ["isError"] = false
                });
            }
            catch (Exception ex)
            {
                SendToolError(idProp, $"Capture failed with exception: {ex.Message}");
            }
        });
    }

    private static List<WindowRecord> EnumerateVisibleWindows(string? filter, bool includeEmptyTitles)
    {
        List<WindowRecord> result = new();
        int enumCount = 0;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            enumCount++;
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            // Skip cloaked windows (hidden virtual desktop / UWP background)
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            StringBuilder classSb = new(256);
            NativeMethods.GetClassName(hWnd, classSb, 256);
            string cls = classSb.ToString();

            // Skip desktop and shell workers
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return true;
            }

            Rectangle rect = WindowCaptureEngine.GetWindowBounds(hWnd);
            if (rect.Width <= 10 || rect.Height <= 10) return true;

            StringBuilder titleSb = new(512);
            NativeMethods.GetWindowText(hWnd, titleSb, 512);
            string title = titleSb.ToString().Trim();

            if (string.IsNullOrEmpty(title) && !includeEmptyTitles)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            string procName = "";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch { }

            if (!string.IsNullOrEmpty(filter))
            {
                bool match = (!string.IsNullOrEmpty(title) && title.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                             (!string.IsNullOrEmpty(procName) && procName.Contains(filter, StringComparison.OrdinalIgnoreCase));
                if (!match) return true;
            }

            result.Add(new WindowRecord(
                Handle: (long)hWnd,
                Title: title,
                ProcessName: procName,
                ProcessId: (int)pid,
                ClassName: cls,
                Bounds: new WindowBounds(rect.X, rect.Y, rect.Width, rect.Height),
                IsMinimized: NativeMethods.IsIconic(hWnd)
            ));

            return true;
        }, nint.Zero);

        return result;
    }

    private static void SendToolSuccess(JsonElement idProp, string text)
    {
        SendResult(idProp, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = false
        });
    }

    private static void SendToolError(JsonElement idProp, string errorMessage)
    {
        SendResult(idProp, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = errorMessage
                }
            },
            ["isError"] = true
        });
    }

    private static void SendResult(JsonElement idProp, JsonNode result)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(idProp.GetRawText()),
            ["result"] = result
        };

        string json = response.ToJsonString();
        Console.WriteLine(json);
        Console.Out.Flush();
    }

    private static void SendError(JsonElement? idProp, int code, string message)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idProp.HasValue ? JsonNode.Parse(idProp.Value.GetRawText()) : null,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        string json = response.ToJsonString();
        Console.WriteLine(json);
        Console.Out.Flush();
    }

    private static T RunOnDefaultDesktop<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;

        Thread thread = new(() =>
        {
            try
            {
                nint hDesk = NativeMethods.OpenDesktop("default", 0, false, NativeMethods.DESKTOP_ALL);
                if (hDesk != nint.Zero)
                {
                    NativeMethods.SetThreadDesktop(hDesk);
                }

                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw error;
        }

        return result;
    }

    private static void RunOnDefaultDesktop(Action action)
    {
        RunOnDefaultDesktop(() =>
        {
            action();
            return true;
        });
    }

    private record WindowRecord(
        long Handle,
        string Title,
        string ProcessName,
        int ProcessId,
        string ClassName,
        WindowBounds Bounds,
        bool IsMinimized);

    private record WindowBounds(int X, int Y, int Width, int Height);
}
