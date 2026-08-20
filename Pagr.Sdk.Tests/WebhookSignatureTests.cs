using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pagr.Sdk.Exceptions;
using Pagr.Sdk.Webhooks;
using Xunit;

namespace Pagr.Sdk.Tests;

/// <summary>
/// Tests for async-render webhook signature verification.
/// </summary>
/// <remarks>
/// The signatures asserted here are built independently from the documented scheme — a raw
/// <see cref="HMACSHA256"/> over the exact request bytes, hex-encoded — and never by calling
/// the SDK's own verifier, since a helper that agrees with itself would prove nothing.
/// The same canonical vectors are asserted by every Pagr SDK, so all of them agree by
/// construction rather than by each re-deriving the scheme.
/// </remarks>
public class WebhookSignatureTests
{
    private const string Secret = "whsec_test-secret";
    private const string OtherSecret = "whsec_someone-elses-secret";
    private const string RotatedInSecret = "whsec_the-new-one";

    /// <summary>The canonical vector's <c>t</c> — 2025-08-11T08:00:00Z.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_754_899_200);

    /// <summary>
    /// The canonical body from parity-contract §9, byte for byte — the spaces after <c>:</c> and
    /// <c>,</c> are part of the signed material.
    /// </summary>
    private const string CompletionBodyText =
        """{"jobId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "state": "completed", "status": "ok", "renderedCount": 2, "requestedCount": 2}""";

    private static readonly byte[] CompletionBody = Encoding.UTF8.GetBytes(CompletionBodyText);

    private const string ProgressBodyText =
        """
        {"jobId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "processed": 1, "requestedCount": 2, "documentIndex": 0, "document": {"id": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "documentName": "Invoice 1", "templateId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "versionNumber": 3, "environment": "test", "fileSizeBytes": 1024, "pageCount": 1, "renderedAt": "2026-08-11T09:00:00Z", "renderDuration": 42.0, "documentType": "Template"}}
        """;

    private static readonly byte[] ProgressBody = Encoding.UTF8.GetBytes(ProgressBodyText);

    /// <summary>Builds an <c>X-Pagr-Signature</c> header the way the Pagr server does.</summary>
    private static string Sign(byte[] body, DateTimeOffset at, params string[] secrets)
    {
        var timestamp = at.ToUnixTimeSeconds();
        var signed = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body).ToArray();

        var parts = new List<string> { $"t={timestamp}" };
        parts.AddRange(secrets.Select(secret => "v1=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed)).ToLowerInvariant()));

        return string.Join(",", parts);
    }

    /// <summary>Signs at <see cref="Now"/>.</summary>
    private static string Sign(byte[] body, params string[] secrets) => Sign(body, Now, secrets);

    // ── The canonical cross-SDK vector ───────────────────────────────────────────

    [Fact]
    public void CanonicalVector_IsProducedAndAccepted()
    {
        // Hardcoded from parity-contract.md §9: if this drifts, every SDK has drifted.
        const string header =
            "t=1754899200,v1=bcaa0dced1702951e44a0c10c9729c853d59433fbb954a8c299e743abd89b2bf";

        // This test's server-side signer produces exactly the contract's header…
        Assert.Equal(header, Sign(CompletionBody, Secret));
        // …and the SDK accepts that literal header.
        WebhookSignature.Verify(CompletionBody, header, Secret, now: Now);
    }

    [Fact]
    public void CanonicalVector_OtherSecretsMatchTheContract()
    {
        Assert.Equal(
            "t=1754899200,v1=471267f764e691c424f4d19583d663595c632be130899c42565d07c216f7446a",
            Sign(CompletionBody, OtherSecret));
        Assert.Equal(
            "t=1754899200,v1=2ec463ea515f6d65cb098c2b65d38e6f54063459a1e3da06f56bd42e70772f33",
            Sign(CompletionBody, RotatedInSecret));
    }

    [Fact]
    public void Constants_MatchTheDocumentedScheme()
    {
        Assert.Equal("X-Pagr-Signature", WebhookSignature.HeaderName);
        Assert.Equal(TimeSpan.FromSeconds(300), WebhookSignature.DefaultTolerance);
    }

    // ── Verify: accepted callbacks ───────────────────────────────────────────────

    [Fact]
    public void Verify_AcceptsSignatureProducedByTheServer()
    {
        WebhookSignature.Verify(CompletionBody, Sign(CompletionBody, Secret), Secret, now: Now);
    }

    [Fact]
    public void Verify_AcceptsAStringBodyIdenticallyToBytes()
    {
        var header = Sign(CompletionBody, Secret);

        WebhookSignature.Verify(CompletionBodyText, header, Secret, now: Now);
    }

    [Fact]
    public void Verify_AcceptsDuringRotation_WhenOnlyTheOldSecretIsHeld()
    {
        // The server signs with both secrets for the grace period, so a receiver that has not
        // switched over yet must still verify — that is what makes rotation non-breaking.
        var header = Sign(CompletionBody, RotatedInSecret, Secret);

        WebhookSignature.Verify(CompletionBody, header, Secret, now: Now);
    }

    [Fact]
    public void Verify_AcceptsARetrySignedWithinTheTolerance()
    {
        // Each retry attempt is re-signed with a fresh timestamp, so a delivery that lands on
        // attempt 4 is not mistaken for a replay.
        var header = Sign(CompletionBody, Now.AddSeconds(-120), Secret);

        WebhookSignature.Verify(CompletionBody, header, Secret, now: Now);
    }

    [Fact]
    public void Verify_IgnoresAnUnknownSchemeVersion()
    {
        // A future v2= alongside v1= must not make the header unparsable.
        var header = Sign(CompletionBody, Secret) + ",v2=deadbeef";

        WebhookSignature.Verify(CompletionBody, header, Secret, now: Now);
    }

    [Fact]
    public void Verify_ToleranceBoundaryIsInclusive()
    {
        WebhookSignature.Verify(
            CompletionBody, Sign(CompletionBody, Now.AddSeconds(-300), Secret), Secret, now: Now);

        Assert.Throws<PagrSignatureException>(() => WebhookSignature.Verify(
            CompletionBody, Sign(CompletionBody, Now.AddSeconds(-301), Secret), Secret, now: Now));
    }

    // ── Verify: rejected callbacks ───────────────────────────────────────────────

    [Fact]
    public void Verify_RejectsATamperedBody()
    {
        var header = Sign(CompletionBody, Secret);
        var tampered = Encoding.UTF8.GetBytes(CompletionBodyText.Replace("completed", "failed!!!"));

        var ex = Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(tampered, header, Secret, now: Now));
        Assert.Contains("matched the configured secret", ex.Message);
    }

    [Fact]
    public void Verify_RejectsAReSerializedBody()
    {
        // The documented footgun: same JSON *value*, different bytes. Worth pinning, because it
        // is the failure every integrator hits first.
        var header = Sign(CompletionBody, Secret);
        var reSerialized = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(CompletionBodyText),
            new JsonSerializerOptions { WriteIndented = true });

        Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(reSerialized, header, Secret, now: Now));
    }

    [Fact]
    public void Verify_RejectsASignatureFromAnotherOrganisation()
    {
        var header = Sign(CompletionBody, OtherSecret);

        Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(CompletionBody, header, Secret, now: Now));
    }

    [Fact]
    public void Verify_RejectsAReplayedCallback()
    {
        var header = Sign(CompletionBody, Now.AddSeconds(-1800), Secret);

        var ex = Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(CompletionBody, header, Secret, now: Now));
        Assert.Contains("outside the", ex.Message);
    }

    [Fact]
    public void Verify_RejectsAFutureDatedCallback()
    {
        // Drift is absolute in both directions, matching the server-side verifier — a far-future
        // t must not buy an attacker an open window.
        var header = Sign(CompletionBody, Now.AddSeconds(1800), Secret);

        var ex = Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(CompletionBody, header, Secret, now: Now));
        Assert.Contains("outside the", ex.Message);
    }

    [Fact]
    public void Verify_ToleranceIsConfigurable()
    {
        var header = Sign(CompletionBody, Now.AddSeconds(-600), Secret);

        WebhookSignature.Verify(
            CompletionBody, header, Secret, tolerance: TimeSpan.FromSeconds(900), now: Now);

        Assert.Throws<PagrSignatureException>(() => WebhookSignature.Verify(
            CompletionBody, header, Secret, tolerance: TimeSpan.FromSeconds(60), now: Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_RejectsAnUnsignedRequest(string? header)
    {
        var ex = Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(CompletionBody, header, Secret, now: Now));
        Assert.Contains("no X-Pagr-Signature header", ex.Message);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("t=notanumber,v1=abc")]
    [InlineData("t=1754899200")]                     // no signature at all
    [InlineData("v1=abc")]                           // no timestamp, so nothing bounds a replay
    [InlineData("t=99999999999999999999,v1=abc")]    // a t no clock could produce
    public void Verify_RejectsAMalformedHeader(string header)
    {
        Assert.Throws<PagrSignatureException>(
            () => WebhookSignature.Verify(CompletionBody, header, Secret, now: Now));
    }

    // ── Verify: a missing secret is a configuration error ────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_AnAbsentSecretIsAConfigurationErrorNotABadSignature(string? secret)
    {
        // Deliberately NOT PagrSignatureException, and deliberately not a silent pass: an unset
        // environment variable must be loud and distinguishable from a forged callback.
        var ex = Assert.Throws<ArgumentException>(() => WebhookSignature.Verify(
            CompletionBody, Sign(CompletionBody, Secret), secret!, now: Now));

        Assert.Contains("signing secret is required", ex.Message);
        Assert.Equal("secret", ex.ParamName);
    }

    [Fact]
    public void Verify_AConfigurationErrorIsNotCaughtByPagrApiException()
    {
        Exception ex = Assert.Throws<ArgumentException>(
            () => WebhookSignature.Verify(CompletionBody, "t=1,v1=a", "", now: Now));

        Assert.IsNotAssignableFrom<PagrApiException>(ex);
    }

    // ── ParseSignedCallback ──────────────────────────────────────────────────────

    [Fact]
    public void ParseSignedCallback_VerifiesAndParsesACompletion()
    {
        var callback = WebhookSignature.ParseSignedCallback(
            CompletionBody, Sign(CompletionBody, Secret), Secret, now: Now);

        var completion = Assert.IsType<RenderCompletion>(callback);
        Assert.Equal(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), completion.JobId);
        Assert.True(completion.Ok);
    }

    [Fact]
    public void ParseSignedCallback_VerifiesAndParsesAProgressCallback()
    {
        var callback = WebhookSignature.ParseSignedCallback(
            ProgressBody, Sign(ProgressBody, Secret), Secret, now: Now);

        var progress = Assert.IsType<RenderProgress>(callback);
        Assert.Equal(0, progress.DocumentIndex);
        Assert.Equal("Invoice 1", progress.Document.DocumentName);
    }

    [Fact]
    public void ParseSignedCallback_AcceptsAStringBody()
    {
        var callback = WebhookSignature.ParseSignedCallback(
            CompletionBodyText, Sign(CompletionBody, Secret), Secret, now: Now);

        Assert.IsType<RenderCompletion>(callback);
    }

    [Fact]
    public void ParseSignedCallback_DoesNotParseAnUnverifiedPayload()
    {
        // The point of the combined helper: a bad signature fails before the body is decoded, so
        // application code never sees a payload that was not proven to come from Pagr.
        Assert.Throws<PagrSignatureException>(() => WebhookSignature.ParseSignedCallback(
            CompletionBody, Sign(CompletionBody, OtherSecret), Secret, now: Now));
    }

    [Fact]
    public void ParseSignedCallback_AVerifiedButUnparsableBodyIsADecodeError()
    {
        var body = Encoding.UTF8.GetBytes("not json at all");

        var ex = Assert.Throws<PagrDecodeException>(() => WebhookSignature.ParseSignedCallback(
            body, Sign(body, Secret), Secret, now: Now));
        Assert.Contains("parse webhook payload", ex.Message);
    }

    [Fact]
    public void ParseSignedCallback_AVerifiedBodyOfTheWrongShapeIsADecodeError()
    {
        var body = Encoding.UTF8.GetBytes(
            """{"jobId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""");

        var ex = Assert.Throws<PagrDecodeException>(() => WebhookSignature.ParseSignedCallback(
            body, Sign(body, Secret), Secret, now: Now));
        Assert.Contains("missing required field(s)", ex.Message);
    }
}
