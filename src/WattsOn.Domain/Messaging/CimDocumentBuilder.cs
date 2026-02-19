using System.Text.Json;
using System.Text.Json.Serialization;

namespace WattsOn.Domain.Messaging;

/// <summary>
/// Builds CIM JSON envelopes (IEC 62325) for outbound DataHub messages.
/// Each document follows the MarketDocument structure with proper headers,
/// sender/receiver identification, and Series (transactions).
/// </summary>
public class CimDocumentBuilder
{
    /// <summary>DataHub GLN — Energinet DataHub as metering point administrator.</summary>
    public const string DataHubGln = "5790001330552";

    /// <summary>A10 = GS1 (GLN) coding scheme.</summary>
    private const string CodingSchemeGln = "A10";

    /// <summary>23 = Electricity business sector.</summary>
    private const string BusinessSectorElectricity = "23";

    /// <summary>DDQ = Balance/energy supplier.</summary>
    private const string RoleSupplier = "DDQ";

    /// <summary>DGL = Metered data responsible / metering point administrator (DataHub).</summary>
    private const string RoleDataHub = "DGL";

    private readonly string _documentType;
    private readonly string _typeCode;
    private readonly string _processType;
    private readonly string _senderGln;
    private readonly string _senderRole;
    private readonly string _receiverGln;
    private readonly string _receiverRole;
    private readonly Guid _documentId;
    private readonly DateTimeOffset _createdDateTime;
    private readonly List<Dictionary<string, object?>> _series = new();

    private CimDocumentBuilder(
        string documentType,
        string typeCode,
        string processType,
        string senderGln,
        string senderRole,
        string receiverGln,
        string receiverRole)
    {
        _documentType = documentType;
        _typeCode = typeCode;
        _processType = processType;
        _senderGln = senderGln;
        _senderRole = senderRole;
        _receiverGln = receiverGln;
        _receiverRole = receiverRole;
        _documentId = Guid.NewGuid();
        _createdDateTime = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Create a builder for the given RSM document configuration.
    /// </summary>
    public static CimDocumentBuilder Create(
        RsmDocumentType rsmType,
        string processType,
        string senderGln,
        string? senderRole = null,
        string? receiverGln = null,
        string? receiverRole = null)
    {
        var config = RsmDocumentConfig.Get(rsmType);
        return new CimDocumentBuilder(
            config.MarketDocumentName,
            config.TypeCode,
            processType,
            senderGln,
            senderRole ?? RoleSupplier,
            receiverGln ?? DataHubGln,
            receiverRole ?? RoleDataHub);
    }

    /// <summary>
    /// Add a Series (transaction) to the document with the given fields.
    /// Each series gets its own auto-generated mRID.
    /// </summary>
    public CimDocumentBuilder AddSeries(Dictionary<string, object?> fields)
    {
        var series = new Dictionary<string, object?>
        {
            ["mRID"] = Guid.NewGuid().ToString()
        };

        foreach (var kvp in fields)
        {
            if (kvp.Value is not null)
                series[kvp.Key] = kvp.Value;
        }

        _series.Add(series);
        return this;
    }

    /// <summary>
    /// Build the CIM JSON envelope as a serialized string.
    /// </summary>
    public string Build()
    {
        var document = new Dictionary<string, object?>
        {
            ["mRID"] = _documentId.ToString(),
            ["type"] = new CimCodeValue(_typeCode),
            ["process.processType"] = new CimCodeValue(_processType),
            ["businessSector.type"] = new CimCodeValue(BusinessSectorElectricity),
            ["sender_MarketParticipant.mRID"] = new CimCodedValue(CodingSchemeGln, _senderGln),
            ["sender_MarketParticipant.marketRole.type"] = new CimCodeValue(_senderRole),
            ["receiver_MarketParticipant.mRID"] = new CimCodedValue(CodingSchemeGln, _receiverGln),
            ["receiver_MarketParticipant.marketRole.type"] = new CimCodeValue(_receiverRole),
            ["createdDateTime"] = _createdDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        if (_series.Count > 0)
        {
            document["Series"] = _series;
        }

        var envelope = new Dictionary<string, object?>
        {
            [_documentType] = document
        };

        return JsonSerializer.Serialize(envelope, CimJsonOptions.Default);
    }

    /// <summary>
    /// CIM value with just a value field: { "value": "..." }
    /// </summary>
    internal record CimCodeValue(string value);

    /// <summary>
    /// CIM value with coding scheme + value: { "codingScheme": "...", "value": "..." }
    /// </summary>
    internal record CimCodedValue(string codingScheme, string value);
}

/// <summary>
/// JSON serializer options tuned for CIM document output.
/// </summary>
internal static class CimJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        PropertyNamingPolicy = null // Preserve exact property names
    };
}

/// <summary>
/// Known RSM document types for outbound messages.
/// </summary>
public enum RsmDocumentType
{
    /// <summary>RSM-001: Request change of supplier (BRS-001, BRS-003)</summary>
    Rsm001,

    /// <summary>RSM-005: Request end of supply / move-out / incorrect move (BRS-002, BRS-010, BRS-011)</summary>
    Rsm005,

    /// <summary>RSM-027: Request change customer characteristics (BRS-015)</summary>
    Rsm027,

    /// <summary>RSM-032: Request charge links (BRS-038)</summary>
    Rsm032,

    /// <summary>RSM-035: Request prices (BRS-034)</summary>
    Rsm035,
}

/// <summary>
/// RSM-specific document configuration (MarketDocument name + type code).
/// </summary>
public static class RsmDocumentConfig
{
    private static readonly Dictionary<RsmDocumentType, RsmConfig> Configs = new()
    {
        [RsmDocumentType.Rsm001] = new("RequestChangeOfSupplier_MarketDocument", "392"),
        [RsmDocumentType.Rsm005] = new("RequestEndOfSupply_MarketDocument", "392"),
        [RsmDocumentType.Rsm027] = new("RequestChangeCustomerCharacteristics_MarketDocument", "D15"),
        [RsmDocumentType.Rsm032] = new("RequestChargeLinks_MarketDocument", "E0G"),
        [RsmDocumentType.Rsm035] = new("RequestPrices_MarketDocument", "E0G"),
    };

    public static RsmConfig Get(RsmDocumentType type) => Configs[type];

    public record RsmConfig(string MarketDocumentName, string TypeCode);
}
