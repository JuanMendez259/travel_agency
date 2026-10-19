using TravelAgency.App.Services;
using Xunit;

namespace TravelAgency.App.Tests;

public class PkceChallengeTests
{
    [Fact]
    public void Challenge_IsS256Base64Url()
    {
        var verifier = PkceChallenge.CreateVerifier();
        var challenge = PkceChallenge.CreateChallenge(verifier);
        Assert.True(challenge.Length == 43);
        Assert.Contains(challenge, c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    [Fact]
    public void Verifier_Is43CharBase64Url()
    {
        var verifier = PkceChallenge.CreateVerifier();
        Assert.Equal(43, verifier.Length);
        Assert.Contains(verifier, c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    [Fact]
    public void Callback_WithMatchingState_ReturnsCode()
    {
        var state = PkceChallenge.CreateState();
        var redirectUrl = $"/callback?state={state}&code=test123";
        var result = PkceChallenge.TryParseAuthCallback(redirectUrl, state, out var code, out var error);
        Assert.True(result);
        Assert.Equal("test123", code);
        Assert.Null(error);
    }

    [Fact]
    public void Callback_WithDifferentState_ReturnsStateError()
    {
        var state = PkceChallenge.CreateState();
        var otherState = PkceChallenge.CreateState();
        var redirectUrl = $"/callback?state={otherState}&code=test123";
        var result = PkceChallenge.TryParseAuthCallback(redirectUrl, state, out var code, out var error);
        Assert.False(result);
        Assert.Equal("state", error);
        Assert.Equal("test123", code);
    }

    [Fact]
    public void Callback_WithoutCode_ReturnsCancelledError()
    {
        var state = PkceChallenge.CreateState();
        var redirectUrl = $"/callback?state={state}";
        var result = PkceChallenge.TryParseAuthCallback(redirectUrl, state, out var code, out var error);
        Assert.False(result);
        Assert.Equal("cancelled", error);
        Assert.Null(code);
    }
}