using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("aliases")]
public class ShimmieAlias
{
    [Key]
    [Column("oldtag")]
    [MaxLength(128)]
    public string OldTag { get; set; } = string.Empty;

    [Column("newtag")]
    [MaxLength(128)]
    public string NewTag { get; set; } = string.Empty;
}
