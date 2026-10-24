using System.Diagnostics;
using System.Text.RegularExpressions;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.LayoutRenderers.Wrappers;

namespace PharmacyDelivery.Api.Infrastructure;

/// <summary>
/// NLog extensions: trace correlation (<c>${trace-id}</c>, <c>${span-id}</c> from the current OpenTelemetry activity),
/// <c>${app-version}</c>, <c>${app-environment}</c> and the <c>${mask-pii:inner=…}</c> wrapper that masks personal data.
/// </summary>
public static class NLogExtensions
{
    public static void Register(ISetupExtensionsBuilder extensions)
    {
        extensions.RegisterLayoutRenderer("trace-id", _ => Activity.Current?.TraceId.ToString() ?? string.Empty);
        extensions.RegisterLayoutRenderer("span-id", _ => Activity.Current?.SpanId.ToString() ?? string.Empty);
        extensions.RegisterLayoutRenderer("app-version", _ => AppInfo.Version);
        extensions.RegisterLayoutRenderer("app-environment", _ => AppInfo.Environment);
        extensions.RegisterLayoutRenderer<MaskPiiLayoutRendererWrapper>("mask-pii");
    }
}

/// <summary>
/// Masks personal data and secrets in log output: email local parts, phone numbers, access tokens in query strings
/// (SignalR passes the JWT as <c>access_token</c>), bearer tokens and password fields.
/// </summary>
[LayoutRenderer("mask-pii")]
[ThreadAgnostic]
public sealed partial class MaskPiiLayoutRendererWrapper : WrapperLayoutRendererBase
{
    public static string Mask(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = AccessTokenRegex().Replace(text, "${key}=***");
        text = BearerRegex().Replace(text, "Bearer ***");
        text = PasswordRegex().Replace(text, "${key}\":\"***\"");
        text = EmailRegex().Replace(text, "***@${domain}");
        return PhoneRegex().Replace(text, "***");
    }

    protected override string Transform(string text) => Mask(text);

    [GeneratedRegex(@"(?<key>access_token|refresh_token|token)=[^&\s""]+", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?<key>""(?:password|currentPassword|newPassword|refreshToken))""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@(?<domain>[A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\+1[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}(?!\d)")]
    private static partial Regex PhoneRegex();
}
