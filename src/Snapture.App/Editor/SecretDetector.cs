using System.Text.RegularExpressions;

namespace Snapture.App.Editor;

/// <summary>
/// Subset of the Gitleaks rule pack (MIT, gitleaks/gitleaks repo) ported as compiled regex.
/// Matches API keys, JWTs, AWS / GCP / Azure credentials, GitHub / Stripe / Slack / Twilio
/// tokens, plus PII (credit cards w/ Luhn validation, SSN, IBAN, IP, MAC, email).
///
/// Each rule has a small false-positive risk; that's acceptable here because the user
/// reviews the proposed redactions in the editor before flattening.
/// </summary>
public static class SecretDetector
{
    public const string RulePackVersion = "2026.1";
    public const string RulePackSource = "gitleaks/gitleaks (MIT) + PII extensions";

    public sealed record SecretRule(string Id, string Description, Regex Pattern, bool LuhnValidate = false);

    public sealed record DetectedSecret(string RuleId, string Description, string Match, int Index, int Length);

    public static IReadOnlyList<SecretRule> Rules { get; } = BuildRules();

    private static IReadOnlyList<SecretRule> BuildRules()
    {
        const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        return new[]
        {
            // AWS — gitleaks "aws-access-token"
            new SecretRule("aws-access-key", "AWS Access Key ID",
                new Regex(@"\b(A3T[A-Z0-9]|AKIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|ASIA)[A-Z0-9]{16}\b", Opts)),
            new SecretRule("aws-secret-key", "AWS Secret Access Key",
                new Regex(@"(?i)aws(.{0,20})?(?-i)['""]?[0-9a-zA-Z\/+]{40}['""]?", Opts)),

            // GCP service-account
            new SecretRule("gcp-api-key", "Google API Key",
                new Regex(@"\bAIza[0-9A-Za-z\-_]{35}\b", Opts)),
            new SecretRule("gcp-service-account", "Google service-account JSON head",
                new Regex(@"\b-----BEGIN PRIVATE KEY-----\b", Opts)),

            // Azure
            new SecretRule("azure-storage", "Azure Storage account key",
                new Regex(@"\b[a-zA-Z0-9+/]{86}==\b", Opts)),

            // GitHub
            new SecretRule("gh-pat", "GitHub Personal Access Token",
                new Regex(@"\bghp_[A-Za-z0-9]{36}\b", Opts)),
            new SecretRule("gh-app", "GitHub App token",
                new Regex(@"\bghs_[A-Za-z0-9]{36}\b", Opts)),
            new SecretRule("gh-oauth", "GitHub OAuth token",
                new Regex(@"\bgho_[A-Za-z0-9]{36}\b", Opts)),
            new SecretRule("gh-refresh", "GitHub refresh token",
                new Regex(@"\bghr_[A-Za-z0-9]{36}\b", Opts)),

            // Stripe
            new SecretRule("stripe-live", "Stripe live key",
                new Regex(@"\bsk_live_[0-9a-zA-Z]{24,}\b", Opts)),
            new SecretRule("stripe-publishable", "Stripe publishable key",
                new Regex(@"\bpk_live_[0-9a-zA-Z]{24,}\b", Opts)),

            // Slack
            new SecretRule("slack-token", "Slack token",
                new Regex(@"\bxox[baprs]-[0-9a-zA-Z\-]{10,}\b", Opts)),
            new SecretRule("slack-webhook", "Slack incoming webhook",
                new Regex(@"https://hooks\.slack\.com/services/[A-Z0-9/]+", Opts)),

            // Twilio
            new SecretRule("twilio-sid", "Twilio Account SID",
                new Regex(@"\bAC[a-z0-9]{32}\b", Opts)),

            // JWT (header.payload.signature shape)
            new SecretRule("jwt", "JSON Web Token",
                new Regex(@"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\b", Opts)),

            // npm token
            new SecretRule("npm-token", "npm token",
                new Regex(@"\bnpm_[A-Za-z0-9]{36}\b", Opts)),

            // Generic 32+ hex
            new SecretRule("hex-secret", "Long hex string (potential secret)",
                new Regex(@"\b[a-fA-F0-9]{40,}\b", Opts)),

            // PII
            new SecretRule("credit-card", "Credit card number (Luhn-validated)",
                new Regex(@"\b(?:\d[ -]*?){13,19}\b", Opts), LuhnValidate: true),
            new SecretRule("ssn-us", "US Social Security Number",
                new Regex(@"\b(?!000|666|9\d\d)\d{3}[-\s]?(?!00)\d{2}[-\s]?(?!0000)\d{4}\b", Opts)),
            new SecretRule("iban", "IBAN",
                new Regex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", Opts)),
            new SecretRule("email", "Email address",
                new Regex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", Opts)),
            new SecretRule("ipv4", "IPv4 address",
                new Regex(@"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b", Opts)),
            new SecretRule("mac", "MAC address",
                new Regex(@"\b(?:[0-9a-fA-F]{2}[:\-]){5}[0-9a-fA-F]{2}\b", Opts)),

            // HIPAA / PHI rule pack — off by default; user enables via Settings → Auto-redact → Healthcare
            new SecretRule("phi-mrn", "Medical Record Number (10-digit prefixed)",
                new Regex(@"\b(?:MRN|MR#|Med\.?\s*Rec\.?)[:\s#]*\d{6,10}\b", Opts)),
            new SecretRule("phi-npi", "National Provider Identifier (Luhn-validated 10-digit)",
                new Regex(@"\b\d{10}\b", Opts), LuhnValidate: true),
            new SecretRule("phi-dea", "DEA Number",
                new Regex(@"\b[A-Z]{2}\d{7}\b", Opts)),
            new SecretRule("phi-dicom-uid", "DICOM UID (StudyInstanceUID / AccessionNumber)",
                new Regex(@"\b\d+(?:\.\d+){4,}\b", Opts)),
            new SecretRule("phi-dob-marker", "Date of Birth marker",
                new Regex(@"(?i)\b(?:DOB|Date\s+of\s+Birth|D\.O\.B\.?)[:\s]*\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}\b", Opts)),
            new SecretRule("phi-patient-marker", "Patient name/ID marker",
                new Regex(@"(?i)\b(?:patient\s+name|patient\s+id|pt\.\s*name)[:\s]+\S+", Opts)),
        };
    }

    public static IEnumerable<DetectedSecret> Scan(string text, ISet<string>? disabledRuleIds = null)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var rule in Rules)
        {
            if (disabledRuleIds is not null && disabledRuleIds.Contains(rule.Id)) continue;
            foreach (Match m in rule.Pattern.Matches(text))
            {
                if (rule.LuhnValidate && !LuhnValid(StripNonDigits(m.Value))) continue;
                yield return new DetectedSecret(rule.Id, rule.Description, m.Value, m.Index, m.Length);
            }
        }
    }

    private static string StripNonDigits(string s) =>
        new(s.Where(char.IsDigit).ToArray());

    private static bool LuhnValid(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;
        int sum = 0; bool dbl = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (dbl) { d *= 2; if (d > 9) d -= 9; }
            sum += d; dbl = !dbl;
        }
        return sum % 10 == 0;
    }
}
