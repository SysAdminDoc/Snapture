using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snapture.App.Services;

public static class DeclarativeUploaderBodyTypes
{
    public const string None = "None";
    public const string MultipartFormData = "MultipartFormData";
    public const string FormUrlEncoded = "FormURLEncoded";
    public const string Json = "JSON";
    public const string Xml = "XML";
    public const string Binary = "Binary";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "multipartformdata" or "multipart/form-data" => MultipartFormData,
        "formurlencoded" or "application/x-www-form-urlencoded" => FormUrlEncoded,
        "json" or "application/json" => Json,
        "xml" or "application/xml" => Xml,
        "binary" or "application/octet-stream" => Binary,
        _ => None
    };
}

/// <summary>
/// A user-owned ShareX-compatible declarative uploader. Profiles are inert until a user selects
/// them from the editor or tray; Snapture never auto-uploads a capture.
/// </summary>
public sealed class DeclarativeUploaderProfile
{
    public string Name { get; set; } = string.Empty;
    public string DestinationType { get; set; } = "ImageUploader";
    public string RequestMethod { get; set; } = "POST";
    public string RequestUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = DeclarativeUploaderBodyTypes.MultipartFormData;
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string FileFormName { get; set; } = "file";
    public string UrlTemplate { get; set; } = string.Empty;
    public string ThumbnailUrlTemplate { get; set; } = string.Empty;
    public string DeletionUrlTemplate { get; set; } = string.Empty;
    public string ErrorMessageTemplate { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;

    public DeclarativeUploaderProfile Clone() => new()
    {
        Name = Name,
        DestinationType = DestinationType,
        RequestMethod = RequestMethod,
        RequestUrl = RequestUrl,
        Parameters = CopyMap(Parameters),
        Headers = CopyMap(Headers),
        Body = Body,
        Arguments = CopyMap(Arguments),
        FileFormName = FileFormName,
        UrlTemplate = UrlTemplate,
        ThumbnailUrlTemplate = ThumbnailUrlTemplate,
        DeletionUrlTemplate = DeletionUrlTemplate,
        ErrorMessageTemplate = ErrorMessageTemplate,
        TimeoutSeconds = TimeoutSeconds
    };

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? RequestUrl : Name;

    private static Dictionary<string, string> CopyMap(Dictionary<string, string>? values) =>
        values is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(values, StringComparer.OrdinalIgnoreCase);
}

public sealed record DeclarativeUploaderRequest(
    byte[] PngBytes,
    string FileName,
    string Source,
    int Width,
    int Height,
    DateTime CapturedAtUtc);

public sealed record DeclarativeUploaderResult(
    bool Succeeded,
    int StatusCode,
    string ResponseBody,
    Uri RequestUri,
    Uri? ResponseUri,
    string? Url,
    string? ThumbnailUrl,
    string? DeletionUrl,
    string? ErrorMessage);

public sealed class DeclarativeUploaderException(string message) : InvalidOperationException(message);

public static class DeclarativeUploaderService
{
    public const int MaxInputBytes = 100 * 1024 * 1024;
    public const int MaxResponseCharacters = 2 * 1024 * 1024;
    public const int MaxTimeoutSeconds = 300;

    private static readonly HttpClient DefaultHttp = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });
    private static readonly Regex TemplateToken = new(@"\{(?<kind>json|header):(?<value>[^{}]+)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex JsonPathPart = new(@"(?:^|\.)(?<name>[^.\[\]]+)|\[(?<index>\d+)\]", RegexOptions.CultureInvariant);

    public static DeclarativeUploaderProfile ImportJson(string json, string? sourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new DeclarativeUploaderException("Uploader JSON must contain one object.");
        var root = document.RootElement;
        string requestUrl = GetString(root, "RequestURL", "RequestUrl") ?? string.Empty;
        string name = GetString(root, "Name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) && Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri))
            name = uri.Host;
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(sourceName))
            name = Path.GetFileNameWithoutExtension(sourceName);

        string destinationType = GetString(root, "DestinationType") ?? "ImageUploader";
        string? bodyType = GetString(root, "Body");
        var profile = new DeclarativeUploaderProfile
        {
            Name = name,
            DestinationType = destinationType,
            RequestMethod = GetString(root, "RequestMethod") ?? "POST",
            RequestUrl = requestUrl,
            Parameters = ReadMap(root, "Parameters"),
            Headers = ReadMap(root, "Headers"),
            Body = bodyType is not null
                ? DeclarativeUploaderBodyTypes.Normalize(bodyType)
                : destinationType.Contains("ImageUploader", StringComparison.OrdinalIgnoreCase)
                    ? DeclarativeUploaderBodyTypes.MultipartFormData
                    : DeclarativeUploaderBodyTypes.None,
            Arguments = ReadMap(root, "Arguments"),
            FileFormName = GetString(root, "FileFormName") ?? "file",
            UrlTemplate = GetString(root, "URL", "Url", "UrlTemplate") ?? string.Empty,
            ThumbnailUrlTemplate = GetString(root, "ThumbnailURL", "ThumbnailUrl", "ThumbnailUrlTemplate") ?? string.Empty,
            DeletionUrlTemplate = GetString(root, "DeletionURL", "DeletionUrl", "DeletionUrlTemplate") ?? string.Empty,
            ErrorMessageTemplate = GetString(root, "ErrorMessage", "ErrorMessageTemplate") ?? string.Empty,
            TimeoutSeconds = GetInt(root, "TimeoutSeconds") ?? 30
        };
        ValidateProfile(profile);
        return profile;
    }

    public static void ValidateProfile(DeclarativeUploaderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new DeclarativeUploaderException("An uploader name is required.");
        if (profile.Name.Trim().Length > 120)
            throw new DeclarativeUploaderException("Uploader names must be 120 characters or fewer.");
        if (!AllowedMethods.Contains(profile.RequestMethod.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new DeclarativeUploaderException("Uploader method must be GET, POST, PUT, PATCH, or DELETE.");
        if (!Uri.TryCreate(profile.RequestUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new DeclarativeUploaderException("Uploader URL must be an absolute HTTP or HTTPS URL.");
        if (profile.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
            throw new DeclarativeUploaderException($"Uploader timeout must be between 1 and {MaxTimeoutSeconds} seconds.");
        if (profile.Parameters is null || profile.Headers is null || profile.Arguments is null)
            throw new DeclarativeUploaderException("Uploader maps cannot be null.");
        if (profile.Parameters.Count > 128 || profile.Headers.Count > 128 || profile.Arguments.Count > 128)
            throw new DeclarativeUploaderException("Uploader profiles may contain at most 128 entries per map.");
        string body = DeclarativeUploaderBodyTypes.Normalize(profile.Body);
        if (body == DeclarativeUploaderBodyTypes.MultipartFormData && string.IsNullOrWhiteSpace(profile.FileFormName))
            throw new DeclarativeUploaderException("Multipart uploaders require a file form name.");
        if (profile.FileFormName.Any(char.IsControl) || profile.FileFormName.Length > 128)
            throw new DeclarativeUploaderException("The multipart file form name is invalid.");
        ValidateMap(profile.Parameters, "parameter");
        ValidateMap(profile.Headers, "header");
        ValidateMap(profile.Arguments, "argument");
        _ = new HttpMethod(profile.RequestMethod.Trim());
    }

    public static async Task<DeclarativeUploaderResult> UploadAsync(
        DeclarativeUploaderProfile profile,
        DeclarativeUploaderRequest request,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        ValidateProfile(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (request.PngBytes.Length == 0 || request.PngBytes.Length > MaxInputBytes)
            throw new DeclarativeUploaderException("The capture is empty or exceeds the 100 MB uploader limit.");
        if (string.IsNullOrWhiteSpace(request.FileName) || request.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new DeclarativeUploaderException("The capture file name is invalid.");

        var timestamp = request.CapturedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        string input = request.FileName;
        string requestUrl = ExpandInputTemplate(profile.RequestUrl, request, input, timestamp);
        requestUrl = AddQueryParameters(requestUrl, profile.Parameters, request, input, timestamp);
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var requestUri))
            throw new DeclarativeUploaderException("The expanded uploader URL is invalid.");

        using var message = new HttpRequestMessage(new HttpMethod(profile.RequestMethod.Trim()), requestUri)
        {
            Content = BuildContent(profile, request, input, timestamp)
        };
        foreach (var pair in profile.Headers)
        {
            string value = ExpandInputTemplate(pair.Value, request, input, timestamp);
            bool added = string.Equals(pair.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)
                && message.Content is not null
                ? message.Content.Headers.TryAddWithoutValidation(pair.Key, value)
                : message.Headers.TryAddWithoutValidation(pair.Key, value);
            if (!added)
                throw new DeclarativeUploaderException($"The header '{pair.Key}' is not valid for an HTTP request.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await (httpClient ?? DefaultHttp).SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Uploader '{profile.Name}' exceeded its {profile.TimeoutSeconds}-second timeout.");
        }

        using (response)
        {
            string responseBody;
            try
            {
                responseBody = Limit(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Uploader '{profile.Name}' exceeded its {profile.TimeoutSeconds}-second timeout.");
            }
            Uri? responseUri = response.RequestMessage?.RequestUri;
            var responseHeaders = response.Headers.Concat(response.Content.Headers)
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => string.Join(", ", group.SelectMany(pair => pair.Value)), StringComparer.OrdinalIgnoreCase);
            string? url = ResolveResponseTemplate(profile.UrlTemplate, responseBody, responseUri, responseHeaders);
            string? thumbnail = ResolveResponseTemplate(profile.ThumbnailUrlTemplate, responseBody, responseUri, responseHeaders);
            string? deletion = ResolveResponseTemplate(profile.DeletionUrlTemplate, responseBody, responseUri, responseHeaders);
            string? error = ResolveResponseTemplate(profile.ErrorMessageTemplate, responseBody, responseUri, responseHeaders);
            bool succeeded = response.IsSuccessStatusCode;
            if (!succeeded && string.IsNullOrWhiteSpace(error))
                error = $"Uploader returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
            return new DeclarativeUploaderResult(
                succeeded,
                (int)response.StatusCode,
                responseBody,
                requestUri,
                responseUri,
                NormalizeReturnedUrl(url),
                NormalizeReturnedUrl(thumbnail),
                NormalizeReturnedUrl(deletion),
                error);
        }
    }

    public static string? ResolveResponseTemplate(
        string? template,
        string responseBody,
        Uri? responseUri = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.IsNullOrWhiteSpace(responseBody) ? null : responseBody.Trim();
        string value = template;
        value = TemplateToken.Replace(value, match =>
        {
            string kind = match.Groups["kind"].Value;
            string key = match.Groups["value"].Value;
            if (kind.Equals("header", StringComparison.OrdinalIgnoreCase))
                return responseHeaders is not null && responseHeaders.TryGetValue(key, out var header) ? header : string.Empty;
            return ReadJsonPath(responseBody, key) ?? string.Empty;
        });
        value = value.Replace("{responseurl}", responseUri?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        value = value.Replace("{response}", responseBody, StringComparison.OrdinalIgnoreCase);
        return value.Trim();
    }

    private static HttpContent? BuildContent(
        DeclarativeUploaderProfile profile,
        DeclarativeUploaderRequest request,
        string input,
        string timestamp)
    {
        string body = DeclarativeUploaderBodyTypes.Normalize(profile.Body);
        var arguments = profile.Arguments.ToDictionary(
            pair => pair.Key,
            pair => ExpandArgumentValue(pair.Value, request, input, timestamp),
            StringComparer.OrdinalIgnoreCase);
        return body switch
        {
            DeclarativeUploaderBodyTypes.None => null,
            DeclarativeUploaderBodyTypes.MultipartFormData => BuildMultipart(profile, request, arguments),
            DeclarativeUploaderBodyTypes.FormUrlEncoded => new FormUrlEncodedContent(arguments),
            DeclarativeUploaderBodyTypes.Json => JsonContent.Create(arguments),
            DeclarativeUploaderBodyTypes.Xml => new StringContent(BuildXml(arguments), Encoding.UTF8, "application/xml"),
            DeclarativeUploaderBodyTypes.Binary => new ByteArrayContent(request.PngBytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("image/png") }
            },
            _ => throw new DeclarativeUploaderException($"Unsupported uploader body '{profile.Body}'.")
        };
    }

    private static MultipartFormDataContent BuildMultipart(
        DeclarativeUploaderProfile profile,
        DeclarativeUploaderRequest request,
        IReadOnlyDictionary<string, string> arguments)
    {
        var content = new MultipartFormDataContent();
        foreach (var pair in arguments)
            content.Add(new StringContent(pair.Value), pair.Key);
        var file = new ByteArrayContent(request.PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, profile.FileFormName, request.FileName);
        return content;
    }

    private static string BuildXml(IReadOnlyDictionary<string, string> arguments)
    {
        var root = new XElement("upload", arguments.Select(pair => new XElement(
            XmlConvertName(pair.Key), pair.Value)));
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string XmlConvertName(string key)
    {
        var builder = new StringBuilder("field");
        foreach (char ch in key)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return builder.ToString();
    }

    private static string AddQueryParameters(
        string requestUrl,
        IReadOnlyDictionary<string, string> parameters,
        DeclarativeUploaderRequest request,
        string input,
        string timestamp)
    {
        if (parameters.Count == 0) return requestUrl;
        var query = string.Join("&", parameters.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(ExpandArgumentValue(pair.Value, request, input, timestamp))));
        return requestUrl.Contains('?', StringComparison.Ordinal)
            ? requestUrl + (requestUrl.EndsWith('?') || requestUrl.EndsWith('&') ? string.Empty : "&") + query
            : requestUrl + "?" + query;
    }

    private static string ExpandInputTemplate(
        string value,
        DeclarativeUploaderRequest request,
        string input,
        string timestamp) => value
        .Replace("{input}", input, StringComparison.OrdinalIgnoreCase)
        .Replace("{filename}", request.FileName, StringComparison.OrdinalIgnoreCase)
        .Replace("{source}", request.Source ?? string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("{width}", request.Width.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("{height}", request.Height.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase);

    private static string ExpandArgumentValue(
        string value,
        DeclarativeUploaderRequest request,
        string input,
        string timestamp)
    {
        string expanded = ExpandInputTemplate(value, request, input, timestamp);
        return expanded.Equals("{input}", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToBase64String(request.PngBytes)
            : expanded;
    }

    private static string? ReadJsonPath(string json, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement current = document.RootElement;
            string normalized = path.Trim();
            if (normalized.StartsWith("$.", StringComparison.Ordinal)) normalized = normalized[2..];
            else if (normalized.StartsWith('$')) normalized = normalized[1..];
            foreach (Match match in JsonPathPart.Matches(normalized))
            {
                if (match.Groups["name"].Success)
                {
                    if (current.ValueKind != JsonValueKind.Object
                        || !current.TryGetProperty(match.Groups["name"].Value, out current))
                        return null;
                }
                else if (match.Groups["index"].Success)
                {
                    if (current.ValueKind != JsonValueKind.Array
                        || !int.TryParse(match.Groups["index"].Value, out int index)
                        || index < 0 || index >= current.GetArrayLength())
                        return null;
                    current = current[index];
                }
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
        }
        catch (JsonException) { return null; }
    }

    private static string? NormalizeReturnedUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https")
            ? uri.ToString()
            : null;

    private static string Limit(string value) => value.Length <= MaxResponseCharacters
        ? value
        : value[..MaxResponseCharacters] + Environment.NewLine + "[response truncated]";

    private static void ValidateMap(IReadOnlyDictionary<string, string> map, string kind)
    {
        foreach (var pair in map)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256)
                throw new DeclarativeUploaderException($"Each uploader {kind} needs a non-empty key of 256 characters or fewer.");
            if (pair.Key.Any(char.IsControl) || pair.Value is null || pair.Value.Any(ch => ch is '\r' or '\n'))
                throw new DeclarativeUploaderException($"Uploader {kind} '{pair.Key}' contains an invalid control character.");
        }
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value)) continue;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        return null;
    }

    private static int? GetInt(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.TryGetInt32(out int result) ? result : null;

    private static Dictionary<string, string> ReadMap(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(root, name, out var value) || value.ValueKind != JsonValueKind.Object)
            return map;
        foreach (var property in value.EnumerateObject())
            map[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.ToString();
        return map;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
}
