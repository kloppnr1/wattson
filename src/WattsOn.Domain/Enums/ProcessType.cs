namespace WattsOn.Domain.Enums;

/// <summary>
/// BRS process type — each maps to a specific DataHub business process.
/// </summary>
public enum ProcessType
{
    /// <summary>BRS-001 — Leverandørskift (Change of Supplier)</summary>
    Leverandørskift = 1,

    /// <summary>BRS-002 — Supplyophør (End of Supply)</summary>
    Supplyophør = 2,

    /// <summary>BRS-005 — Anmodning om stamdata (Request for Master Data)</summary>
    StamdataAnmodning = 5,

    /// <summary>BRS-009 — Tilflytning (Move-In)</summary>
    Tilflytning = 9,

    /// <summary>BRS-010 — Fraflytning (Move-Out)</summary>
    Fraflytning = 10,

    /// <summary>BRS-015 — Opdatering af customerstamdata (Update Customer Master Data)</summary>
    CustomerStamdataOpdatering = 15,

    /// <summary>BRS-021 — Fremsendelse af måledata (Submission of Metered Data)</summary>
    MåledataFremsendelse = 21,

    /// <summary>BRS-027 — Fremsendelse af beregnede engrosydelser (Wholesale Services)</summary>
    EngrosYdelserFremsendelse = 27,

    /// <summary>BRS-031 — Opdatering af prices (Update of Prices)</summary>
    PrisOpdatering = 31,

    /// <summary>BRS-003 — Fejlagtigt leverandørskift (Incorrect Supplier Switch)</summary>
    FejlagtigtLeverandørskift = 3,

    /// <summary>BRS-011 — Fejlagtig flytning (Incorrect Move)</summary>
    FejlagtigFlytning = 11,

    /// <summary>BRS-034 — Anmodning om priser (Request for Prices)</summary>
    PrisAnmodning = 34,

    /// <summary>BRS-038 — Anmodning om pristilknytninger (Request for Charge Links)</summary>
    PristilknytningAnmodning = 38,

    /// <summary>BRS-023 outbound — Anmodning om aggregeret data (Request Aggregated Measure Data)</summary>
    AggregetDataAnmodning = 23,

    /// <summary>BRS-027 outbound — Anmodning om engrosafregning (Request Wholesale Settlement)</summary>
    EngrosAfregningAnmodning = 270,

    /// <summary>BRS-024 — Anmodning om årssum (Request Yearly Consumption Sum)</summary>
    ÅrssumAnmodning = 24,

    /// <summary>BRS-025 — Anmodning om måledata (Request Historical Metered Data)</summary>
    MåledataAnmodning = 25,

    /// <summary>BRS-039 — Serviceydelse (Service Request)</summary>
    Serviceydelse = 39,

    /// <summary>BRS-041 — Elvarme (Electrical Heating)</summary>
    Elvarme = 41,

    /// <summary>BRS-004 — Oprettelse af målepunkt (New Metering Point)</summary>
    MålepunktOprettelse = 4,

    /// <summary>BRS-007 — Nedlæggelse af målepunkt (Closedown of Metering Point)</summary>
    MålepunktNedlæggelse = 7,

    /// <summary>BRS-008 — Tilslutning af målepunkt (Connection of Metering Point)</summary>
    MålepunktTilslutning = 8,

    /// <summary>BRS-013 — Afbrydelse/Gentilslutning (Disconnect/Reconnect)</summary>
    AfbrydelseGentilslutning = 13,

    /// <summary>BRS-036 — Ændring af aftagepligt (Product Obligation Change)</summary>
    AftagepligtÆndring = 36,

    /// <summary>BRS-044 — Tvunget leverandørskift (Mandatory Supplier Switch)</summary>
    TvungetLeverandørskift = 44,
}
