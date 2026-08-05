using System.Net.Sockets;

namespace BankingDomain.IntegrationTests;

/// <summary>
/// Integration test cần Docker (Testcontainers). Máy dev không có Docker — hoặc testhost không
/// truy cập được socket — thì skip chứ không fail: fail đỏ vì thiếu hạ tầng làm mất giá trị
/// tín hiệu của test suite. Trên CI (Linux runner có Docker) test chạy đầy đủ.
/// </summary>
public static class DockerAvailability
{
    private static readonly Lazy<bool> _available = new(Probe);

    public static bool IsAvailable => _available.Value;

    public const string SkipReason =
        "Docker không truy cập được từ test host — integration test chỉ chạy khi có Docker (CI).";

    /// <summary>Trả reason để Skip.IfNot(...) dùng; null nghĩa là chạy được.</summary>
    public static string? SkipIfUnavailable() => IsAvailable ? null : SkipReason;

    private static bool Probe()
    {
        foreach (var path in CandidateSockets())
        {
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(path));
                return true;
            }
            catch
            {
                // thử socket kế tiếp
            }
        }
        return false;
    }

    private static IEnumerable<string> CandidateSockets()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(fromEnv) && fromEnv.StartsWith("unix://"))
            yield return fromEnv["unix://".Length..];

        yield return "/var/run/docker.sock";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker/run/docker.sock");
    }
}
