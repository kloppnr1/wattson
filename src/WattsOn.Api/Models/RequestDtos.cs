using WattsOn.Domain.ValueObjects;

namespace WattsOn.Api.Models;

record CreateSupplierIdentityRequest(string Gln, string Name, string? Cvr, bool IsActive = true);

record PatchSupplierIdentityRequest(bool? IsActive = null, string? Name = null, string? Cvr = null);

record AddressDto(string StreetName, string BuildingNumber, string PostCode, string CityName,
    string? Floor = null, string? Suite = null);

record CreateCustomerRequest(string Name, string? Cpr, string? Cvr, Guid SupplierIdentityId, string? Email, string? Phone, AddressDto? Address);

record CreateMeteringPointRequest(string Gsrn, string Type, string Art, string SettlementMethod,
    string Resolution, string GridArea, string GridCompanyGln, AddressDto? Address);

record CreateSupplyRequest(Guid MeteringPointId, Guid CustomerId,
    DateTimeOffset SupplyStart, DateTimeOffset? SupplyEnd);

record MarkInvoicedRequest(string ExternalInvoiceReference);

record PricePointDto(DateTimeOffset Timestamp, decimal Price);

record CreatePrisRequest(
    string ChargeId,
    string OwnerGln,
    string Type,
    string Description,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool VatExempt = false,
    string? PriceResolution = null,
    List<PricePointDto>? PricePoints = null);

record CreatePriceLinkRequest(
    Guid MeteringPointId,
    Guid PriceId,
    DateTimeOffset LinkFrom,
    DateTimeOffset? LinkTo);

record ConfirmSettlementRequest(string ExternalInvoiceReference);

record SimulateSupplierChangeRequest(
    string Gsrn,
    DateTimeOffset EffectiveDate,
    string CustomerName,
    string? CprNumber = null,
    string? CvrNumber = null,
    string? Email = null,
    string? Phone = null,
    AddressDto? Address = null,
    string? PreviousSupplierGln = null,
    string? GridCompanyGln = null,
    string? GridArea = null,
    bool GenerateConsumption = true);

record SimulateOutgoingSupplierChangeRequest(
    Guid SupplyId,
    DateTimeOffset EffectiveDate,
    string? NewSupplierGln = null);

record SimulateMoveInRequest(
    string Gsrn,
    DateTimeOffset EffectiveDate,
    string CustomerName,
    string? CprNumber = null,
    string? CvrNumber = null,
    string? Email = null,
    string? Phone = null,
    AddressDto? Address = null,
    string? GridCompanyGln = null,
    string? GridArea = null,
    bool GenerateConsumption = true);

record SimulateMoveOutRequest(
    Guid SupplyId,
    DateTimeOffset EffectiveDate);

record CreateTimeSeriesRequest(
    Guid MeteringPointId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Resolution,
    string? TransactionId,
    List<ObservationDto> Observations);

record ObservationDto(DateTimeOffset Timestamp, decimal KWh, string? Quality);

record EndOfSupplyRequest(string Gsrn, DateTimeOffset DesiredEndDate, string? Reason);

record MoveOutRequest(string Gsrn, DateTimeOffset EffectiveDate);

record CustomerUpdateRequest(
    string Gsrn,
    DateTimeOffset EffectiveDate,
    string CustomerName,
    string? Cpr,
    string? Cvr,
    string? Email,
    string? Phone,
    AddressDto? Address);

record IncorrectSwitchRequest(string Gsrn, DateTimeOffset SwitchDate, string? Reason);

record IncorrectMoveRequest(string Gsrn, DateTimeOffset MoveDate, string MoveType, string? Reason);

record RequestPricesRequest(
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate = null,
    string? PriceOwnerGln = null,
    string? PriceType = null,
    string? ChargeId = null,
    string RequestType = "E0G");

record RequestChargeLinksRequest(
    string Gsrn,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate = null);
