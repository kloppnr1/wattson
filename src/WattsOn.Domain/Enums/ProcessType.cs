namespace WattsOn.Domain.Enums;

/// <summary>
/// BRS process type — each maps to a specific DataHub business process.
/// </summary>
public enum ProcessType
{
    /// <summary>BRS-001 — Leverandørskift (Change of Supplier)</summary>
    Leverandørskift = 1,

    /// <summary>BRS-002 — Leveranceophør (End of Supply)</summary>
    Leveranceophør = 2,

    /// <summary>BRS-005 — Anmodning om stamdata (Request for Master Data)</summary>
    StamdataAnmodning = 5,

    /// <summary>BRS-009 — Tilflytning (Move-In)</summary>
    Tilflytning = 9,

    /// <summary>BRS-010 — Fraflytning (Move-Out)</summary>
    Fraflytning = 10,

    /// <summary>BRS-015 — Opdatering af kundestamdata (Update Customer Master Data)</summary>
    KundeStamdataOpdatering = 15,

    /// <summary>BRS-021 — Fremsendelse af måledata (Submission of Metered Data)</summary>
    MåledataFremsendelse = 21,

    /// <summary>BRS-027 — Fremsendelse af beregnede engrosydelser (Wholesale Services)</summary>
    EngrosYdelserFremsendelse = 27,

    /// <summary>BRS-031 — Opdatering af priser (Update of Prices)</summary>
    PrisOpdatering = 31,

    /// <summary>BRS-034 — Anmodning om priser (Request for Prices)</summary>
    PrisAnmodning = 34
}
