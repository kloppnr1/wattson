using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("EXU_FLEXBHISTORYTABLE", Schema = "dbo")]
public class FlexBillingHistoryTable
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("METERINGPOINT")]
    [StringLength(30)]
    public string MeteringPoint { get; set; } = null!;

    [Column("DELIVERYCATEGORY")]
    [StringLength(10)]
    public string DeliveryCategory { get; set; } = null!;

    [Column("HISTKEYNUMBER")]
    [StringLength(10)]
    public string HistKeyNumber { get; set; } = null!;

    [Column("REQSTARTDATE", TypeName = "datetime")]
    public DateTime ReqStartDate { get; set; }

    [Column("REQENDDATE", TypeName = "datetime")]
    public DateTime ReqEndDate { get; set; }

    [Column("BILLINGLOGNUM")]
    [StringLength(10)]
    public string BillingLogNum { get; set; } = null!;

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}