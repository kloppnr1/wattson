using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WattsOn.Migration.XellentData.Entities;

[Table("EXU_PRODUCTEXTENDTABLE", Schema = "dbo")]
public class ExuProductExtendTable
{
    [Column("DATAAREAID")]
    [StringLength(4)]
    public string DataAreaId { get; set; } = null!;

    [Column("PRODUCTNUM")]
    [StringLength(20)]
    public string Productnum { get; set; } = null!;

    [Column("PRODUCTTYPE")]
    [StringLength(20)]
    public string Producttype { get; set; } = null!;

    [Column("STARTDATE", TypeName = "datetime")]
    public DateTime Startdate { get; set; }

    [Column("ENDDATE", TypeName = "datetime")]
    public DateTime Enddate { get; set; }

    [Key]
    [Column("RECID")]
    public long RecId { get; set; }
}