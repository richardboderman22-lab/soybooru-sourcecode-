using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("image_tag_categories")]
public class ShimmieTagCategory
{
    [Key]
    [Column("category")]
    [MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [Column("display_singular")]
    [MaxLength(60)]
    public string? DisplaySingular { get; set; }

    [Column("display_multiple")]
    [MaxLength(60)]
    public string? DisplayMultiple { get; set; }

    [Column("color")]
    [MaxLength(7)]
    public string? Color { get; set; }
}
