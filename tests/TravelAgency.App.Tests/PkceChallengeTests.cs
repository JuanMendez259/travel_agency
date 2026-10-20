using TravelAgency.App.Services;
using Xunit;

namespace TravelAgency.App.Tests;

public class PkceChallengeTests
{
    [Fact]
    public void Challenge_IsS256Base64Url_KnownVector()
    {
        var challenge = PkceChallenge.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void Verifier_Is43CharBase64Url()
    {
        var verifier = PkceChallenge.CreateVerifier();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", verifier);
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