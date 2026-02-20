using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("EXU_FLEXBHISTORYLINE", Schema = "dbo")]
public class FlexBillingHistoryLine
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("HISTKEYNUMBER")]
    [StringLength(10)]
    public string HistKeyNumber { get; set; } = null!;

    [Column("DATETIME24HOUR", TypeName = "datetime")]
    public DateTime DateTime24Hour { get; set; }

    [Column("TIMEVALUE", TypeName = "numeric(28, 12)")]
    public decimal TimeValue { get; set; }

    [Column("POWEREXCHANGEPRICE", TypeName = "numeric(28, 12)")]
    public decimal PowerExchangePrice { get; set; }

    [Column("CALCULATEDPRICE", TypeName = "numeric(28, 12)")]
    public decimal CalculatedPrice { get; set; }

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}