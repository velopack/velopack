#nullable enable
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// The Azure Trusted Signing metadata.json file (the same format the Azure.CodeSigning.Dlib signtool plugin reads).
/// </summary>
public sealed class AzureTrustedSigningMetadata
{
    private static readonly (string Name, Action<DefaultAzureCredentialOptions, bool> Apply)[] ExcludableCredentials = [
        ("EnvironmentCredential", (o, v) => o.ExcludeEnvironmentCredential = v),
        ("WorkloadIdentityCredential", (o, v) => o.ExcludeWorkloadIdentityCredential = v),
        ("ManagedIdentityCredential", (o, v) => o.ExcludeManagedIdentityCredential = v),
#pragma warning disable CS0618 // SharedTokenCacheCredential is obsolete in Azure.Identity, but the Dlib metadata format accepts it
        ("SharedTokenCacheCredential", (o, v) => o.ExcludeSharedTokenCacheCredential = v),
#pragma warning restore CS0618
        ("VisualStudioCredential", (o, v) => o.ExcludeVisualStudioCredential = v),
        ("VisualStudioCodeCredential", (o, v) => o.ExcludeVisualStudioCodeCredential = v),
        ("AzureCliCredential", (o, v) => o.ExcludeAzureCliCredential = v),
        ("AzurePowerShellCredential", (o, v) => o.ExcludeAzurePowerShellCredential = v),
        ("AzureDeveloperCliCredential", (o, v) => o.ExcludeAzureDeveloperCliCredential = v),
        ("InteractiveBrowserCredential", (o, v) => o.ExcludeInteractiveBrowserCredential = v),
    ];

    /// <summary>The account endpoint, e.g. https://eus.codesigning.azure.net (normalized: has a scheme, no trailing slash).</summary>
    public string Endpoint { get; }

    public string CodeSigningAccountName { get; }

    public string CertificateProfileName { get; }

    /// <summary>Optional id sent with each sign request (x-correlation-id) to correlate requests in Azure logs.</summary>
    public string? CorrelationId { get; }

    /// <summary>Names of DefaultAzureCredential sources to skip.</summary>
    public IReadOnlyList<string> ExcludeCredentials { get; }

    /// <summary>A pre-acquired bearer token for https://codesigning.azure.net; used instead of DefaultAzureCredential.</summary>
    public string? AccessToken { get; }

    public Uri EndpointUri => new(Endpoint);

    private AzureTrustedSigningMetadata(string endpoint, string accountName, string profileName, string? correlationId,
        IReadOnlyList<string> excludeCredentials, string? accessToken)
    {
        Endpoint = endpoint;
        CodeSigningAccountName = accountName;
        CertificateProfileName = profileName;
        CorrelationId = correlationId;
        ExcludeCredentials = excludeCredentials;
        AccessToken = accessToken;
    }

    /// <summary>Reads and validates a metadata.json file.</summary>
    public static AzureTrustedSigningMetadata Load(string path)
    {
        if (!File.Exists(path)) {
            throw new UserInfoException($"Azure Trusted Signing metadata file '{path}' does not exist.");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Parses and validates metadata.json content. <paramref name="displayPath"/> is only used in error messages.</summary>
    public static AzureTrustedSigningMetadata Parse(string json, string displayPath)
    {
        MetadataJson? model;
        try {
            model = JsonSerializer.Deserialize<MetadataJson>(
                json,
                new JsonSerializerOptions {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        } catch (JsonException ex) {
            throw new UserInfoException($"Unable to parse Azure Trusted Signing metadata file '{displayPath}': {ex.Message}");
        }

        model ??= new MetadataJson();
        var missing = new List<string>();
        if (String.IsNullOrWhiteSpace(model.Endpoint)) missing.Add(nameof(MetadataJson.Endpoint));
        if (String.IsNullOrWhiteSpace(model.CodeSigningAccountName)) missing.Add(nameof(MetadataJson.CodeSigningAccountName));
        if (String.IsNullOrWhiteSpace(model.CertificateProfileName)) missing.Add(nameof(MetadataJson.CertificateProfileName));
        if (missing.Count > 0) {
            throw new UserInfoException(
                $"Azure Trusted Signing metadata file '{displayPath}' is missing required properties: {String.Join(", ", missing)}.");
        }

        string endpoint = NormalizeEndpoint(model.Endpoint!, displayPath);
        var excludeCredentials = (model.ExcludeCredentials ?? []).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
        string? accessToken = String.IsNullOrWhiteSpace(model.AccessToken) ? null : model.AccessToken!.Trim();
        if (accessToken == null) {
            // validate up front, so a typo is reported before anything is signed
            BuildCredentialOptions(excludeCredentials);
        }

        return new AzureTrustedSigningMetadata(
            endpoint,
            model.CodeSigningAccountName!.Trim(),
            model.CertificateProfileName!.Trim(),
            String.IsNullOrWhiteSpace(model.CorrelationId) ? null : model.CorrelationId!.Trim(),
            excludeCredentials,
            accessToken);
    }

    /// <summary>
    /// Creates the credential used to authenticate: a static token credential when <see cref="AccessToken"/> is set
    /// (ExcludeCredentials is then ignored, like the Dlib), otherwise a DefaultAzureCredential.
    /// </summary>
    public TokenCredential CreateCredential()
    {
        if (AccessToken != null) {
            // the token cannot be refreshed, so report a long expiry (like the Dlib) to stop the SDK from discarding it
            return new AccessTokenCredential(AccessToken, DateTimeOffset.UtcNow.AddDays(1));
        }

        return new DefaultAzureCredential(BuildCredentialOptions(ExcludeCredentials));
    }

    /// <summary>
    /// Maps ExcludeCredentials names (case-insensitive, with or without the "Credential" suffix) onto
    /// DefaultAzureCredentialOptions. On Windows, like the Dlib (which only ran there), every credential not listed is
    /// explicitly enabled. Elsewhere the SDK defaults are kept for the rest, so SharedTokenCacheCredential and
    /// InteractiveBrowserCredential stay off: the first fails the whole chain (instead of falling through to e.g.
    /// AzureCliCredential) when there is no keyring to persist its cache in, and the second can wait forever for a
    /// browser sign-in on a headless machine.
    /// </summary>
    /// <param name="excludeCredentials">Credential names to skip.</param>
    /// <param name="enableAllCredentials">Enable every credential not listed; defaults to true on Windows only.</param>
    public static DefaultAzureCredentialOptions BuildCredentialOptions(IEnumerable<string> excludeCredentials, bool? enableAllCredentials = null)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();
        foreach (var raw in excludeCredentials) {
            string name = raw.Trim();
            var match = ExcludableCredentials.FirstOrDefault(c =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || c.Name.Equals(name + "Credential", StringComparison.OrdinalIgnoreCase));
            if (match.Name == null) {
                invalid.Add(raw);
            } else {
                excluded.Add(match.Name);
            }
        }

        if (invalid.Count > 0) {
            throw new UserInfoException(
                $"Invalid ExcludeCredentials value(s) in Azure Trusted Signing metadata: {String.Join(", ", invalid)}. " +
                $"Valid values are: {String.Join(", ", ExcludableCredentials.Select(c => c.Name))}.");
        }

        bool enableAll = enableAllCredentials ?? OperatingSystem.IsWindows();
        var options = new DefaultAzureCredentialOptions();
        foreach (var (name, apply) in ExcludableCredentials) {
            if (excluded.Contains(name)) {
                apply(options, true);
            } else if (enableAll) {
                apply(options, false);
            }
        }

        return options;
    }

    private static string NormalizeEndpoint(string endpoint, string displayPath)
    {
        endpoint = endpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal)) {
            endpoint = "https://" + endpoint;
        }

        endpoint = endpoint.TrimEnd('/');
        // https only: the SDK's bearer token policy refuses to send a token to a non-TLS endpoint
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) {
            throw new UserInfoException($"Azure Trusted Signing metadata file '{displayPath}' has an invalid Endpoint '{endpoint}'.");
        }

        return endpoint;
    }

    private sealed class MetadataJson
    {
        public string? Endpoint { get; set; }
        public string? CodeSigningAccountName { get; set; }
        public string? CertificateProfileName { get; set; }
        public string? CorrelationId { get; set; }
        public List<string?>? ExcludeCredentials { get; set; }
        public string? AccessToken { get; set; }
    }
}
