using System.Windows;
using System.Windows.Shell;
using System.IO;
using Serilog;

namespace Snapture.App.Services;

internal sealed record JumpListTaskDefinition(
    string Title,
    string Description,
    string Arguments,
    string ApplicationPath);

/// <summary>Registers taskbar jump-list capture verbs without changing shell defaults.</summary>
internal static class JumpListService
{
    internal static IReadOnlyList<JumpListTaskDefinition> BuildTasks(string executablePath)
    {
        string exe = Path.GetFullPath(executablePath);
        return new[]
        {
            new JumpListTaskDefinition("New region", "Capture a selectable screen region", "--new-region", exe),
            new JumpListTaskDefinition("New window", "Capture the foreground window", "--new-window", exe),
            new JumpListTaskDefinition("New fullscreen", "Capture the full virtual screen", "--new-fullscreen", exe)
        };
    }

    public static void Apply()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || Application.Current is null)
                return;

            var jumpList = new JumpList
            {
                ShowFrequentCategory = false,
                ShowRecentCategory = false
            };
            foreach (var task in BuildTasks(exe))
            {
                jumpList.JumpItems.Add(new JumpTask
                {
                    ApplicationPath = task.ApplicationPath,
                    Arguments = task.Arguments,
                    Description = task.Description,
                    Title = task.Title,
                    IconResourcePath = task.ApplicationPath,
                    IconResourceIndex = 0,
                    CustomCategory = "Snapture"
                });
            }
            JumpList.SetJumpList(Application.Current, jumpList);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "JumpList.Apply.Failed");
        }
    }
}
