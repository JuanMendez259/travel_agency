using TravelAgency.Shared.Models;
using Xunit;

namespace TravelAgency.App.Tests;

public sealed class RoomBookingRulesTests
{
    [Theory]
    [InlineData(2, 2, 0, 1)]
    [InlineData(2, 1, 1, 1)]
    [InlineData(2, 4, 0, 2)]
    [InlineData(3, 1, 2, 1)]
    public void Validate_ReturnsRoomsForValidDistribution(int occupancy, int adults, int children, int expectedRooms)
    {
        var error = RoomBookingRules.Validate(occupancy, true, adults, children, out var rooms);

        Assert.Null(error);
        Assert.Equal(expectedRooms, rooms);
    }

    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(2, 3, 0)]
    [InlineData(2, 0, 2)]
    public void Validate_RejectsInvalidRoomDistribution(int occupancy, int adults, int children)
    {
        var error = RoomBookingRules.Validate(occupancy, true, adults, children, out var rooms);

        Assert.NotNull(error);
        Assert.Null(rooms);
    }

    [Fact]
    public void Validate_AllowsOptionsWithoutRoomOccupancy()
    {
        var error = RoomBookingRules.Validate(null, true, 2, 1, out var rooms);

        Assert.Null(error);
        Assert.Null(rooms);
    }

    [Fact]
    public void Validate_RejectsRoomOccupancyWhenTripDoesNotIncludeHotel()
    {
        var error = RoomBookingRules.Validate(2, false, 2, 0, out var rooms);

        Assert.NotNull(error);
        Assert.Null(rooms);
    }
}
