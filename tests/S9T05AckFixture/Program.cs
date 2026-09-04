using System.Diagnostics;
using System;
using System.IO;
using System.Text.Json;
var root = args[1]; var op = Environment.GetEnvironmentVariable("S9_T05_OPERATION_ID") ?? args[^1]; var p = Process.GetCurrentProcess(); var version = Environment.GetEnvironmentVariable("S9_T05_ACK_VERSION") ?? "1.0.0";
var dir = Path.Combine(root, "updates", op); Directory.CreateDirectory(dir);
if (Environment.GetEnvironmentVariable("S9_T05_NORMAL_LAUNCH") == "1") File.WriteAllText(Path.Combine(dir, "normal-launch.marker"), version);
if (Environment.GetEnvironmentVariable("S9_T05_ACK_MODE") is "hold" && Environment.GetEnvironmentVariable("S9_T05_ACK_VERSION") is null) { File.WriteAllText(Path.Combine(dir, "candidate-ready.marker"), "ready"); System.Threading.Thread.Sleep(1500); }
if (Environment.GetEnvironmentVariable("S9_T05_ACK_MODE") is "old-fail" || Environment.GetEnvironmentVariable("S9_T05_ACK_VERSION") is null && Environment.GetEnvironmentVariable("S9_T05_ACK_MODE") is "malformed") { File.WriteAllText(Path.Combine(dir, "health-ack.json"), "{"); return; }
if (Environment.GetEnvironmentVariable("S9_T05_ACK_VERSION") is null && Environment.GetEnvironmentVariable("S9_T05_ACK_MODE") is "wrong-type") { File.WriteAllText(Path.Combine(dir, "health-ack.json"), "{\"pid\":\"bad\"}"); return; }
File.WriteAllText(Path.Combine(dir, "health-ack.json"), JsonSerializer.Serialize(new { operationId = op, version, pid = p.Id, startedUtc = p.StartTime.ToUniversalTime().ToString("O"), migrationCount = 9, lastMigration = "20260901155124_AddPolicyAndBaselineFoundation", integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
