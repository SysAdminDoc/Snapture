using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class JumpListServiceTests
{
    [TestMethod]
    public void BuildsCaptureTasksWithStableInteractiveArguments()
    {
        var tasks = JumpListService.BuildTasks(@"C:\Program Files\Snapture\Snapture.App.exe");

        Assert.HasCount(3, tasks);
        CollectionAssert.AreEquivalent(
            new[] { "--new-region", "--new-window", "--new-fullscreen" },
            tasks.Select(task => task.Arguments).ToArray());
        Assert.IsTrue(tasks.All(task => task.ApplicationPath.EndsWith("Snapture.App.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(tasks.All(task => !string.IsNullOrWhiteSpace(task.Description)));
    }
}
