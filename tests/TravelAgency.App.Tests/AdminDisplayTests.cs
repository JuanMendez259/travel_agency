using TravelAgency.App.Modules.Admin;
using Xunit;

namespace TravelAgency.App.Tests;

public class AdminDisplayTests
{
    [Theory]
    [InlineData("María López", "ML")]
    [InlineData("Jorge Ramírez", "JR")]
    [InlineData("Ana", "A")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void Initials_ReturnsUpToTwoLetters(string? name, string expected)
    {
        Assert.Equal(expected, AdminDisplay.Initials(name));
    }
}
