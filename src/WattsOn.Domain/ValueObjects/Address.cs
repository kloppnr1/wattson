using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Danish address — used for customers and metering point locations.
/// </summary>
public class Address : ValueObject
{
    public string StreetName { get; }
    public string BuildingNumber { get; }
    public string? Floor { get; }
    public string? Suite { get; }
    public string PostCode { get; }
    public string CityName { get; }
    public string? MunicipalityCode { get; }
    public string CountryCode { get; }

    private Address(
        string streetName, string buildingNumber,
        string? floor, string? suite,
        string postCode, string cityName,
        string? municipalityCode, string countryCode)
    {
        StreetName = streetName;
        BuildingNumber = buildingNumber;
        Floor = floor;
        Suite = suite;
        PostCode = postCode;
        CityName = cityName;
        MunicipalityCode = municipalityCode;
        CountryCode = countryCode;
    }

    public static Address Create(
        string streetName, string buildingNumber,
        string postCode, string cityName,
        string? floor = null, string? suite = null,
        string? municipalityCode = null, string countryCode = "DK")
    {
        if (string.IsNullOrWhiteSpace(streetName))
            throw new ArgumentException("Street name is required.", nameof(streetName));
        if (string.IsNullOrWhiteSpace(postCode))
            throw new ArgumentException("Post code is required.", nameof(postCode));

        return new Address(streetName, buildingNumber, floor, suite, postCode, cityName, municipalityCode, countryCode);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StreetName;
        yield return BuildingNumber;
        yield return Floor;
        yield return Suite;
        yield return PostCode;
        yield return CityName;
        yield return MunicipalityCode;
        yield return CountryCode;
    }

    public override string ToString()
    {
        var parts = new List<string> { $"{StreetName} {BuildingNumber}" };
        if (!string.IsNullOrEmpty(Floor)) parts.Add($"{Floor}.");
        if (!string.IsNullOrEmpty(Suite)) parts.Add(Suite);
        parts.Add($"{PostCode} {CityName}");
        return string.Join(", ", parts);
    }
}
