using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("EXU_PRICEELEMENTTABLE", Schema = "dbo")]
public class PriceElementTable
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("PARTYCHARGETYPEID")]
    [StringLength(10)]
    public string PartyChargeTypeId { get; set; } = null!;

    [Column("CHARGETYPECODE")]
    public int ChargeTypeCode { get; set; }

    [Column("DESCRIPTION")]
    [StringLength(50)]
    public string Description { get; set; } = null!;

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}