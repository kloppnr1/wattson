using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("INVENTTABLE", Schema = "dbo")]
public class InventTable
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("ITEMID")]
    [StringLength(20)]
    public string ItemId { get; set; } = null!;

    [Column("ITEMTYPE")]
    public int ItemType { get; set; }

    [Column("EXU_USERATEFROMFLEXPRICING")]
    public int ExuUseRateFromFlexPricing { get; set; }

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}