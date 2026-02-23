using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[PrimaryKey(nameof(Dataareaid), nameof(Recid))]
[Table("EXU_RATETABLE", Schema = "dbo")]
public class ExuRateTable
{
    [Key]
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string Dataareaid { get; set; } = null!;

    [Column("RATETYPE")]
    [StringLength(20)]
    public string Ratetype { get; set; } = null!;

    [Column("PRODUCTNUM")]
    [StringLength(20)]
    public string Productnum { get; set; } = null!;

    [Column("DELIVERYCATEGORY")]
    [StringLength(10)]
    public string Deliverycategory { get; set; } = null!;

    [Column("COMPANYID")]
    [StringLength(10)]
    public string Companyid { get; set; } = null!;

    [Column("STARTDATE", TypeName = "datetime")]
    public DateTime Startdate { get; set; }

    [Column("RATE", TypeName = "numeric(28, 12)")]
    public decimal Rate { get; set; }

    [Column("ACCOUNTRATE", TypeName = "numeric(28, 12)")]
    public decimal Accountrate { get; set; }

    [Key]
    [Column("RECID")]
    public long Recid { get; set; }
}
