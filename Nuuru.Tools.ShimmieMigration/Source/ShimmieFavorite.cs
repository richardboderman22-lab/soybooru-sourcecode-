using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("user_favorites")]
public class ShimmieFavorite
{
    [Column("image_id")]
    public int ImageId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("ImageId")]
    public ShimmieImage? Image { get; set; }

    [ForeignKey("UserId")]
    public ShimmieUser? User { get; set; }
}
