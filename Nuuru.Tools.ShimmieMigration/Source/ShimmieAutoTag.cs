using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("auto_tag")]
public class ShimmieAutoTag
{
    [Key]
    [Column("tag")]
    [MaxLength(128)]
    public string Tag { get; set; } = string.Empty;

    [Column("additional_tags")]
    [MaxLength(2000)]
    public string AdditionalTags { get; set; } = string.Empty;
}
