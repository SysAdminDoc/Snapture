using System.Text;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class VideoRingBufferServiceTests
{
    [TestMethod]
    public void SelectRecentStart_ReturnsZeroWhenBufferIsShorterThanRequest()
    {
        var start = VideoRingBufferService.SelectRecentStart(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(30));

        Assert.AreEqual(TimeSpan.Zero, start);
    }

    [TestMethod]
    public void SelectRecentStart_LeavesRequestedTailDuration()
    {
        var start = VideoRingBufferService.SelectRecentStart(
            TimeSpan.FromSeconds(88),
            TimeSpan.FromSeconds(60));

        Assert.AreEqual(TimeSpan.FromSeconds(28), start);
    }

    [TestMethod]
    public void BuildRecentSegmentPlan_UsesTailAcrossSegmentBoundary()
    {
        var plan = VideoSegmentService.BuildRecentSegmentPlan(
            new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(12) },
            TimeSpan.FromSeconds(40));

        Assert.HasCount(2, plan);
        Assert.AreEqual(1, plan[0].Index);
        Assert.AreEqual(TimeSpan.FromSeconds(2), plan[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(30), plan[0].End);
        Assert.AreEqual(2, plan[1].Index);
        Assert.AreEqual(TimeSpan.Zero, plan[1].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(12), plan[1].End);
    }

    [TestMethod]
    public void Recovery_RetainsInterruptedFragmentedSessionWithoutOpeningIt()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string session = VideoRingBufferRecovery.CreateSessionDirectory(root);
            string segmentPath = Path.Combine(session, "segment-001.mp4");
            WriteFragmentedFixture(segmentPath, includeMovieFragment: true);
            VideoRingBufferRecovery.WriteManifest(session, new RingBufferSessionManifest
            {
                SessionId = Path.GetFileName(session),
                State = RingBufferSessionState.Recording,
                SourceMode = "Monitor",
                StartedUtc = DateTime.UtcNow,
                Segments =
                {
                    new RingBufferSegmentManifest
                    {
                        FileName = "segment-001.mp4",
                        StartedUtc = DateTime.UtcNow,
                        SizeBytes = new FileInfo(segmentPath).Length,
                        State = "active"
                    }
                }
            });

            var result = VideoRingBufferRecovery.RecoverOrphans(root, DateTime.UtcNow);

            Assert.AreEqual(1, result.RetainedCount);
            Assert.AreEqual(0, result.DiscardedCount);
            Assert.IsTrue(result.Message.Contains("manual review", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(segmentPath));
            Assert.IsTrue(VideoRingBufferRecovery.HasRecoveries(root));
            Assert.AreEqual(
                RingBufferSessionState.Recovered,
                VideoRingBufferRecovery.TryReadManifest(session)!.State);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public void Recovery_RetainsFailedSaveSessionForExplicitReview()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string session = VideoRingBufferRecovery.CreateSessionDirectory(root);
            string segmentPath = Path.Combine(session, "segment-001.mp4");
            WriteFragmentedFixture(segmentPath, includeMovieFragment: true);
            VideoRingBufferRecovery.WriteManifest(session, new RingBufferSessionManifest
            {
                SessionId = Path.GetFileName(session),
                State = RingBufferSessionState.RecoveryRequired,
                LastError = "trim failed",
                StartedUtc = DateTime.UtcNow,
                Segments =
                {
                    new RingBufferSegmentManifest
                    {
                        FileName = "segment-001.mp4",
                        StartedUtc = DateTime.UtcNow,
                        SizeBytes = new FileInfo(segmentPath).Length,
                        State = "complete"
                    }
                }
            });

            var result = VideoRingBufferRecovery.RecoverOrphans(root, DateTime.UtcNow);

            Assert.AreEqual(1, result.RetainedCount);
            Assert.IsTrue(result.Message.Contains("manual review", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(
                RingBufferSessionState.Recovered,
                VideoRingBufferRecovery.TryReadManifest(session)!.State);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public void Recovery_DiscardsCorruptAndExpiredSessions()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string corrupt = VideoRingBufferRecovery.CreateSessionDirectory(root);
            File.WriteAllBytes(Path.Combine(corrupt, "segment-001.mp4"), new byte[512]);
            VideoRingBufferRecovery.WriteManifest(corrupt, new RingBufferSessionManifest
            {
                SessionId = Path.GetFileName(corrupt),
                State = RingBufferSessionState.RecoveryRequired,
                LastError = "trim failed",
                StartedUtc = DateTime.UtcNow,
                Segments =
                {
                    new RingBufferSegmentManifest { FileName = "segment-001.mp4", State = "complete" }
                }
            });

            string expired = VideoRingBufferRecovery.CreateSessionDirectory(root);
            string expiredSegment = Path.Combine(expired, "segment-001.mp4");
            WriteFragmentedFixture(expiredSegment, includeMovieFragment: true);
            VideoRingBufferRecovery.WriteManifest(expired, new RingBufferSessionManifest
            {
                SessionId = Path.GetFileName(expired),
                State = RingBufferSessionState.Recording,
                StartedUtc = DateTime.UtcNow.AddHours(-3),
                LastUpdatedUtc = DateTime.UtcNow.AddHours(-3),
                Segments =
                {
                    new RingBufferSegmentManifest { FileName = "segment-001.mp4", State = "active" }
                }
            });
            var expiredManifest = VideoRingBufferRecovery.TryReadManifest(expired)!;
            expiredManifest.LastUpdatedUtc = DateTime.UtcNow.AddHours(-3);
            VideoRingBufferRecovery.WriteManifest(expired, expiredManifest);
            expiredManifest = VideoRingBufferRecovery.TryReadManifest(expired)!;
            expiredManifest.LastUpdatedUtc = DateTime.UtcNow.AddHours(-3);
            File.WriteAllText(VideoRingBufferRecovery.GetManifestPath(expired),
                System.Text.Json.JsonSerializer.Serialize(expiredManifest));

            var result = VideoRingBufferRecovery.RecoverOrphans(root, DateTime.UtcNow);

            Assert.AreEqual(0, result.RetainedCount);
            Assert.AreEqual(2, result.DiscardedCount);
            Assert.IsFalse(Directory.Exists(corrupt));
            Assert.IsFalse(Directory.Exists(expired));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public void PruneSegments_EnforcesCountAndByteBudget()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string session = VideoRingBufferRecovery.CreateSessionDirectory(root);
            var manifest = new RingBufferSessionManifest
            {
                SessionId = Path.GetFileName(session),
                State = RingBufferSessionState.Recording
            };
            for (int i = 1; i <= 4; i++)
            {
                string fileName = $"segment-{i:000}.mp4";
                File.WriteAllBytes(Path.Combine(session, fileName), new byte[100]);
                manifest.Segments.Add(new RingBufferSegmentManifest
                {
                    FileName = fileName,
                    StartedUtc = DateTime.UtcNow.AddSeconds(-i),
                    CompletedUtc = DateTime.UtcNow.AddSeconds(-i),
                    DurationSeconds = 30,
                    SizeBytes = 100,
                    State = "complete"
                });
            }

            var removed = VideoRingBufferRecovery.PruneSegments(
                session,
                manifest,
                maximumSegments: 2,
                maximumBytes: 250,
                DateTime.UtcNow);

            Assert.HasCount(2, removed);
            Assert.HasCount(2, manifest.Segments);
            Assert.IsTrue(manifest.Segments.All(segment => File.Exists(Path.Combine(session, segment.FileName))));
            Assert.IsLessThanOrEqualTo(250, manifest.Segments.Sum(segment => segment.SizeBytes));
            Assert.IsTrue(VideoRingBufferRecovery.HasSufficientDiskSpace(100, 100));
            Assert.IsFalse(VideoRingBufferRecovery.HasSufficientDiskSpace(99, 100));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Snapture-ring-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void WriteFragmentedFixture(string path, bool includeMovieFragment)
    {
        byte[] bytes = new byte[512];
        Encoding.ASCII.GetBytes("ftyp").CopyTo(bytes, 32);
        if (includeMovieFragment)
            Encoding.ASCII.GetBytes("moof").CopyTo(bytes, 160);
        File.WriteAllBytes(path, bytes);
    }
}
