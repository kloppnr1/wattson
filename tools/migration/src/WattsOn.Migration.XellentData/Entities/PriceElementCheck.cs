using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("EXU_PRICEELEMENTCHECK", Schema = "dbo")]
public class PriceElementCheck
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("METERINGPOINTID")]
    [StringLength(30)]
    public string MeteringPointId { get; set; } = null!;

    [Column("DELIVERYCATEGORY")]
    [StringLength(10)]
    public string DeliveryCategory { get; set; } = null!;

    [Column("CUSTACCOUNT")]
    [StringLength(25)]
    public string CustAccount { get; set; } = null!;

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}