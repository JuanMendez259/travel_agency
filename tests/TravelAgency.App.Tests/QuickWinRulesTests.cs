using TravelAgency.App.Services;
using TravelAgency.Shared.Models;
using Xunit;

namespace TravelAgency.App.Tests;

public class QuickWinRulesTests
{
    [Fact]
    public void NormalizePhone_PrefixesMexicoCountryCodeToTenDigits()
    {
        Assert.Equal("528110000000", PhoneNumberFormatter.Normalize("811-000-0000"));
    }

    [Fact]
    public void NormalizePhone_PreservesInternationalMexicoNumber()
    {
        Assert.Equal("5214444579256", PhoneNumberFormatter.Normalize("+52 1 444 457 9256"));
    }

    [Fact]
    public void NormalizePhone_RejectsMissingOrTooShortNumber()
    {
        Assert.Null(PhoneNumberFormatter.Normalize(null));
        Assert.Null(PhoneNumberFormatter.Normalize("123-456"));
    }

    [Fact]
    public void BuildWhatsAppUrl_NormalizesPhoneAndEncodesMessage()
    {
        Assert.Equal(
            "https://wa.me/528110000000?text=Hola%2C%20Ana%3F",
            AgencyContacts.BuildWhatsAppUrl("811-000-0000", "Hola, Ana?"));
    }

    [Fact]
    public void BuildPhoneUrl_ReturnsNullWhenPhoneIsMissing()
    {
        Assert.Null(AgencyContacts.BuildPhoneUrl(null));
        Assert.Equal("tel:528110000000", AgencyContacts.BuildPhoneUrl("8110000000"));
    }

    [Fact]
    public void CanOfferContact_OnlyForUncheckedBookingDuringTripDates()
    {
        var trip = new Trip
        {
            StartDate = new DateTime(2026, 10, 20, 8, 0, 0),
            EndDate = new DateTime(2026, 10, 22, 20, 0, 0)
        };
        var booking = new Booking { Status = BookingStatus.Confirmed };

        Assert.True(TripContactPolicy.CanOfferContact(trip, booking, new DateTime(2026, 10, 20)));
        Assert.True(TripContactPolicy.CanOfferContact(trip, booking, new DateTime(2026, 10, 21)));
    }

    [Fact]
    public void CanOfferContact_RejectsCheckedInCancelledFinalizedOrOutOfDateBookings()
    {
        var trip = new Trip
        {
            StartDate = new DateTime(2026, 10, 20),
            EndDate = new DateTime(2026, 10, 22)
        };
        var eligible = new Booking { Status = BookingStatus.Confirmed };

        Assert.False(TripContactPolicy.CanOfferContact(trip, eligible, new DateTime(2026, 10, 19)));
        Assert.False(TripContactPolicy.CanOfferContact(trip, eligible, new DateTime(2026, 10, 23)));
        Assert.False(TripContactPolicy.CanOfferContact(trip, new Booking { Status = BookingStatus.Confirmed, CheckedIn = true }, new DateTime(2026, 10, 20)));
        Assert.False(TripContactPolicy.CanOfferContact(trip, new Booking { Status = BookingStatus.Cancelled }, new DateTime(2026, 10, 20)));
        trip.Finalized = true;
        Assert.False(TripContactPolicy.CanOfferContact(trip, eligible, new DateTime(2026, 10, 20)));
    }

    [Fact]
    public void BuildIcs_UsesFloatingLocalDatesAndEscapesText()
    {
        var trip = new Trip
        {
            Id = 42,
            Title = "Ruta, río; cascada",
            Destination = "Tamasopo, S.L.P.",
            Description = "Línea 1\nLínea 2",
            StartDate = new DateTime(2026, 10, 20, 8, 30, 0),
            EndDate = new DateTime(2026, 10, 20, 19, 0, 0)
        };

        var ics = TripCalendarIcsFormatter.Build(trip, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Contains("UID:trip-42@proximaparadatours\r\n", ics);
        Assert.Contains("DTSTART:20261020T083000\r\n", ics);
        Assert.Contains("DTEND:20261020T190000\r\n", ics);
        Assert.Contains("SUMMARY:Ruta\\, río\\; cascada\r\n", ics);
        Assert.Contains("LOCATION:Tamasopo\\, S.L.P.\r\n", ics);
        Assert.Contains("DESCRIPTION:Línea 1\\nLínea 2\r\n", ics);
        Assert.Contains("DTSTAMP:20260901T120000Z\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }
}
