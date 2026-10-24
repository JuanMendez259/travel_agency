using TravelAgency.Shared.Models;
using Xunit;

public class TripImageRulesTests
{
    [Fact]
    public void HasCapacity_AllowsTenth_RejectsEleventh()
    {
        Assert.True(TripImageRules.HasCapacity(9, 1));
        Assert.False(TripImageRules.HasCapacity(10, 1));
        Assert.False(TripImageRules.HasCapacity(10, 0));
    }

    [Fact]
    public void NextOrder_EmptyIsOne_AndUsesMaxPlusOne()
    {
        Assert.Equal(1, TripImageRules.NextOrder(Array.Empty<TripImage>()));
        Assert.Equal(4, TripImageRules.NextOrder(new[]
        {
            new TripImage { Id = 1, Order = 3 },
            new TripImage { Id = 2, Order = 2 },
        }));
    }

    [Fact]
    public void NormalizeOrder_SortsAndReassignsContiguousOrders()
    {
        var images = new List<TripImage>
        {
            new() { Id = 1, Order = 9 },
            new() { Id = 2, Order = 3 },
            new() { Id = 3, Order = 7 },
        };

        TripImageRules.NormalizeOrder(images);

        Assert.Equal(new[] { 2, 3, 1 }, images.Select(i => i.Id));
        Assert.Equal(new[] { 1, 2, 3 }, images.Select(i => i.Order));
    }

    [Fact]
    public void ChooseCover_PrefersExistingCover_ThenLowestOrder()
    {
        var cover = new TripImage { Id = 2, Order = 5, IsCover = true };
        var first = new TripImage { Id = 1, Order = 1 };
        Assert.Same(cover, TripImageRules.ChooseCover(new[] { first, cover }));

        cover.IsCover = false;
        Assert.Same(first, TripImageRules.ChooseCover(new[] { cover, first }));
    }

    [Fact]
    public void IsValidOrder_RequiresExactSetWithoutDuplicates()
    {
        Assert.True(TripImageRules.IsValidOrder(new[] { 3, 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(TripImageRules.IsValidOrder(new[] { 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(TripImageRules.IsValidOrder(new[] { 1, 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(TripImageRules.IsValidOrder(new[] { 1, 2, 4 }, new[] { 1, 2, 3 }));
    }

    [Fact]
    public void BuildGalleryUrls_UsesOrderedImages_ThenFallsBackToImageUrl()
    {
        var trip = new Trip
        {
            ImageUrl = "cover.jpg",
            Images = new List<TripImage>
            {
                new() { Url = "second.jpg", Order = 2 },
                new() { Url = "first.jpg", Order = 1 },
            }
        };
        Assert.Equal(new[] { "first.jpg", "second.jpg" }, TripImageRules.BuildGalleryUrls(trip));

        trip.Images.Clear();
        Assert.Equal(new[] { "cover.jpg" }, TripImageRules.BuildGalleryUrls(trip));

        trip.ImageUrl = null;
        Assert.Empty(TripImageRules.BuildGalleryUrls(trip));
    }

    [Theory]
    [InlineData("foto.jpg", 100, true)]
    [InlineData("foto.JPEG", 100, true)]
    [InlineData("foto.png", 100, true)]
    [InlineData("foto.webp", 100, true)]
    [InlineData("foto.heic", 100, false)]
    [InlineData("foto.gif", 100, false)]
    [InlineData("foto.jpg", 0, false)]
    [InlineData("foto.jpg", 20L * 1024 * 1024 + 1, false)]
    public void IsAllowed_ValidatesExtensionAndSize(string name, long length, bool expected)
    {
        Assert.Equal(expected, TripImageFileRules.IsAllowed(name, length));
    }
}
