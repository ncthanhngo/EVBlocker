using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EVBlocker.Core.Updates;

namespace EVBlocker.App.Services;

public sealed record UpdateCheckResult
{
    public required bool UpdateAvailable { get; init; }
    public required string Message { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? LatestTag { get; init; }
}

/// <summary>Release metadata, as much of it as this needs.</summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Asks GitHub whether a newer release exists.
/// </summary>
/// <remarks>
/// Only when the user asks. This application exists to stop programs reaching the internet
/// without permission, and one that quietly contacted a server at every launch to ask about
/// itself would be doing the thing it was installed to prevent. The check is a button.
///
/// It reports and links; it never downloads or replaces anything. Self-updating an unsigned
/// executable that manages firewall policy is a larger and riskier feature than it looks -
/// verifying what was downloaded, replacing a running file, and recovering from a half-finished
/// swap - and none of it is needed to tell somebody a new version is out.
/// </remarks>
internal static class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/ncthanhngo/EVBlocker/releases/latest";

    public const string ReleasesPageUrl = "https://github.com/ncthanhngo/EVBlocker/releases";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = Timeout };

        // GitHub rejects requests without one.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EVBlocker", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(LatestReleaseUrl, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A repository with no published release answers 404, which is not a failure.
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    Message = "Chưa có bản phát hành nào trên GitHub.",
                    ReleaseUrl = ReleasesPageUrl,
                };
            }

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            GitHubRelease? release = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);

            if (release is null || release.Draft || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    Message = "Không đọc được thông tin bản phát hành.",
                    ReleaseUrl = ReleasesPageUrl,
                };
            }

            bool newer = ReleaseVersion.IsNewerThan(release.TagName, CurrentVersion);

            return new UpdateCheckResult
            {
                UpdateAvailable = newer,
                LatestTag = release.TagName,
                ReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl,
                Message = newer
                    ? $"Có bản mới: {release.TagName}{(release.Prerelease ? " (bản thử nghiệm)" : string.Empty)}. "
                      + $"Bạn đang dùng {CurrentVersion.ToString(3)}."
                    : $"Đang dùng bản mới nhất ({CurrentVersion.ToString(3)}).",
            };
        }
        catch (HttpRequestException ex)
        {
            // Entirely expected on a machine whose outbound traffic this app is blocking.
            return new UpdateCheckResult
            {
                UpdateAvailable = false,
                Message = $"Không kết nối được tới GitHub: {ex.Message}",
                ReleaseUrl = ReleasesPageUrl,
            };
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult
            {
                UpdateAvailable = false,
                Message = "Quá thời gian chờ khi kiểm tra bản mới.",
                ReleaseUrl = ReleasesPageUrl,
            };
        }
        catch (JsonException)
        {
            return new UpdateCheckResult
            {
                UpdateAvailable = false,
                Message = "GitHub trả về dữ liệu không đọc được.",
                ReleaseUrl = ReleasesPageUrl,
            };
        }
    }
}
