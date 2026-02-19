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

    /// <summary>DDZ = Metering point administrator (DataHub) — used for most RSM types.</summary>
    private const string RoleDataHub = "DDZ";

    private readonly string _documentType;
    private readonly string _typeCode;
    private readonly string _processType;
    private readonly string _senderGln;
    private readonly string _senderRole;
    private readonly string _receiverGln;
    private readonly string _receiverRole;
    private readonly Guid _documentId;
    private readonly DateTimeOffset _createdDateTime;
    private readonly string _transactionElementName;
    private readonly List<Dictionary<string, object?>> _transactions = new();

    private CimDocumentBuilder(
        string documentType,
        string typeCode,
        string processType,
        string senderGln,
        string senderRole,
        string receiverGln,
        string receiverRole,
        string transactionElementName)
    {
        _documentType = documentType;
        _typeCode = typeCode;
        _processType = processType;
        _senderGln = senderGln;
        _senderRole = senderRole;
        _receiverGln = receiverGln;
        _receiverRole = receiverRole;
        _transactionElementName = transactionElementName;
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
            receiverRole ?? config.ReceiverRole ?? RoleDataHub,
            config.TransactionElementName);
    }

    /// <summary>
    /// Add a transaction (MktActivityRecord / Series) to the document.
    /// Each transaction gets its own auto-generated mRID.
    /// The element name varies per RSM type (MktActivityRecord for RSM-005, Series for RSM-016, etc.)
    /// </summary>
    public CimDocumentBuilder AddTransaction(Dictionary<string, object?> fields)
    {
        var transaction = new Dictionary<string, object?>
        {
            ["mRID"] = Guid.NewGuid().ToString()
        };

        foreach (var kvp in fields)
        {
            if (kvp.Value is not null)
                transaction[kvp.Key] = kvp.Value;
        }

        _transactions.Add(transaction);
        return this;
    }

    /// <summary>Alias for AddTransaction — backwards compatibility.</summary>
    public CimDocumentBuilder AddSeries(Dictionary<string, object?> fields) => AddTransaction(fields);

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

        if (_transactions.Count > 0)
        {
            document[_transactionElementName] = _transactions;
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

    /// <summary>RSM-016: Request aggregated measure data (BRS-023 outbound)</summary>
    Rsm016,

    /// <summary>RSM-017: Request wholesale settlement (BRS-027 outbound)</summary>
    Rsm017,

    /// <summary>RSM-015: Request validated measure data (BRS-024/BRS-025 outbound)</summary>
    Rsm015,

    /// <summary>RSM-020: Request service (BRS-005/BRS-039 outbound)</summary>
    Rsm020,
}

/// <summary>
/// RSM-specific document configuration.
/// MarketDocument name, type code, transaction element name, and receiver role
/// per the DataHub RSM specification documents.
/// </summary>
public static class RsmDocumentConfig
{
    private static readonly Dictionary<RsmDocumentType, RsmConfig> Configs = new()
    {
        // RSM-001: MarketDocument type=392, transaction=MktActivityRecord
        // Verified from DataHub doc: "Request change of supplier MarketDocument type = 392"
        [RsmDocumentType.Rsm001] = new("RequestChangeOfSupplier_MarketDocument", "392", "MktActivityRecord", "DDZ"),

        // RSM-005: MarketDocument type=432, transaction=MktActivityRecord
        // Verified from RSM-005 PDF: "Request End of supply med MarketDocument type = 432"
        // Receiver role = DDZ (målepunktsadministrator)
        [RsmDocumentType.Rsm005] = new("RequestEndOfSupply_MarketDocument", "432", "MktActivityRecord", "DDZ"),

        // RSM-027: MarketDocument type=D15, transaction=MktActivityRecord
        // From RSM-027 doc: "MarketDocument type = D15 (Request change Customer characteristics)"
        [RsmDocumentType.Rsm027] = new("RequestChangeCustomerCharacteristics_MarketDocument", "D15", "MktActivityRecord", "DDZ"),

        // RSM-032: Request charge links — pending verification from RSM-032 PDF
        [RsmDocumentType.Rsm032] = new("RequestChargeLinks_MarketDocument", "E0G", "MktActivityRecord", "DDZ"),

        // RSM-035: Request prices — pending verification from RSM-035 PDF
        [RsmDocumentType.Rsm035] = new("RequestPrices_MarketDocument", "E0G", "MktActivityRecord", "DDZ"),

        // RSM-016: Request aggregated measure data — BRS-023 outbound
        // MarketDocument type E74, transaction=Series, receiver=DGL (metered data aggregator)
        [RsmDocumentType.Rsm016] = new("RequestAggregatedMeasureData_MarketDocument", "E74", "Series", "DGL"),

        // RSM-017: Request wholesale settlement — BRS-027 outbound
        // MarketDocument type D21, transaction=Series, receiver=DGL (metered data aggregator)
        [RsmDocumentType.Rsm017] = new("RequestWholesaleSettlement_MarketDocument", "D21", "Series", "DGL"),

        // RSM-015: Request validated measure data — BRS-024/BRS-025 outbound
        // MarketDocument type E73, transaction=Series, receiver=DGL (metered data aggregator)
        [RsmDocumentType.Rsm015] = new("RequestValidatedMeasureData_MarketDocument", "E73", "Series", "DGL"),

        // RSM-020: Request service — BRS-005/BRS-039 outbound
        // MarketDocument type D42, transaction=MktActivityRecord, receiver=DDZ
        [RsmDocumentType.Rsm020] = new("RequestService_MarketDocument", "D42", "MktActivityRecord", "DDZ"),
    };

    public static RsmConfig Get(RsmDocumentType type) => Configs[type];

    /// <param name="MarketDocumentName">Root element name in CIM JSON</param>
    /// <param name="TypeCode">MarketDocument type code</param>
    /// <param name="TransactionElementName">Transaction/series element name (MktActivityRecord or Series)</param>
    /// <param name="ReceiverRole">DataHub receiver role code (DDZ for most, may vary)</param>
    public record RsmConfig(string MarketDocumentName, string TypeCode, string TransactionElementName, string? ReceiverRole = null);
}
