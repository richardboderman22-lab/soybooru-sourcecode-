using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("image_tags")]
public class ShimmieImageTag
{
    [Column("image_id")]
    public int ImageId { get; set; }

    [Column("tag_id")]
    public int TagId { get; set; }

    [ForeignKey("ImageId")]
    public ShimmieImage? Image { get; set; }

    [ForeignKey("TagId")]
    public ShimmieTag? Tag { get; set; }
}
