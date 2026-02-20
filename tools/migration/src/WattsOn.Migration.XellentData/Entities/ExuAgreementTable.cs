using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[PrimaryKey(nameof(Dataareaid), nameof(Deliverycategory), nameof(Companyid), nameof(Agreementnum))]
[Table("EXU_AGREEMENTTABLE", Schema = "dbo")]
public class ExuAgreementTable
{
    [Key]
    [Column("AGREEMENTNUM")]
    [StringLength(10)]
    public string Agreementnum { get; set; } = null!;

    [Key]
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string Dataareaid { get; set; } = null!;

    [Key]
    [Column("COMPANYID")]
    [StringLength(4)]
    public string Companyid { get; set; } = null!;

    [Key]
    [Column("DELIVERYCATEGORY")]
    [StringLength(10)]
    public string Deliverycategory { get; set; } = null!;

    [Column("STARTDATE", TypeName = "datetime")]
    public DateTime Startdate { get; set; }

    [Column("ENDDATE", TypeName = "datetime")]
    public DateTime Enddate { get; set; }

    [Column("RECID")]
    public long Recid { get; set; }
}
