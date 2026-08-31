using System.IO;
using Microsoft.Win32;

namespace StoreExpiryInspector.Infrastructure;

public interface IAutoStartRegistry
{
    string? Read();

    void Write(string command);

    void Delete();
}

public sealed record AutoStartResult(bool Succeeded, bool IsEnabled, string? ErrorMessage);

public sealed class WindowsAutoStartService
{
    public const string ValueName = "StoreExpiryInspector";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IAutoStartRegistry _registry;
    private readonly string _expectedCommand;

    public WindowsAutoStartService(IAutoStartRegistry registry, string executablePath)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _expectedCommand = $"\"{Path.GetFullPath(executablePath)}\"";
    }

    public AutoStartResult ReadState()
    {
        try
        {
            return new(
                true,
                string.Equals(_registry.Read(), _expectedCommand, StringComparison.OrdinalIgnoreCase),
                null);
        }
        catch (Exception exception)
        {
            return new(false, false, exception.Message);
        }
    }

    public AutoStartResult SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _registry.Write(_expectedCommand);
            }
            else
            {
                _registry.Delete();
            }

            return ReadState();
        }
        catch (Exception exception)
        {
            return new(false, false, exception.Message);
        }
    }
}

public sealed class CurrentUserRunRegistry : IAutoStartRegistry
{
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsAutoStartService.RunKeyPath);
        return key?.GetValue(WindowsAutoStartService.ValueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(WindowsAutoStartService.RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户开机启动注册表项。");
        key.SetValue(WindowsAutoStartService.ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsAutoStartService.RunKeyPath, writable: true);
        key?.DeleteValue(WindowsAutoStartService.ValueName, throwOnMissingValue: false);
    }
}
