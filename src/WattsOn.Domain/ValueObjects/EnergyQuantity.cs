using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Represents a quantity of energy with its unit.
/// All DataHub quantities are in kWh with up to 3 decimal places.
/// </summary>
public class EnergyQuantity : ValueObject
{
    /// <summary>Energy amount in kWh</summary>
    public decimal Value { get; }

    /// <summary>Unit — always kWh for Danish market</summary>
    public string Unit { get; } = "kWh";

    private EnergyQuantity(decimal value) => Value = value;

    public static EnergyQuantity Create(decimal value) => new(Math.Round(value, 3));

    public static EnergyQuantity Zero => new(0m);

    public EnergyQuantity Add(EnergyQuantity other) => new(Value + other.Value);
    public EnergyQuantity Subtract(EnergyQuantity other) => new(Value - other.Value);
    public EnergyQuantity Negate() => new(-Value);

    public static EnergyQuantity operator +(EnergyQuantity a, EnergyQuantity b) => a.Add(b);
    public static EnergyQuantity operator -(EnergyQuantity a, EnergyQuantity b) => a.Subtract(b);
    public static EnergyQuantity operator -(EnergyQuantity a) => a.Negate();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value:F3} {Unit}";
}
