using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("EXU_PRICEELEMENTCHECKDATA", Schema = "dbo")]
public class PriceElementCheckData
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("PRICEELEMENTCHECKREFRECID")]
    public long PriceElementCheckRefRecId { get; set; }

    [Column("PARTYCHARGETYPEID")]
    [StringLength(10)]
    public string PartyChargeTypeId { get; set; } = null!;

    [Column("CHARGETYPECODE")]
    public int ChargeTypeCode { get; set; }

    [Column("STARTDATE", TypeName = "datetime")]
    public DateTime StartDate { get; set; }

    [Column("ENDDATE", TypeName = "datetime")]
    public DateTime EndDate { get; set; }

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}