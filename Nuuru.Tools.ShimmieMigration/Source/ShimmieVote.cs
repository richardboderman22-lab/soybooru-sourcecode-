using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("numeric_score_votes")]
public class ShimmieVote
{
    [Column("image_id")]
    public int ImageId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("score")]
    public int Score { get; set; }

    [ForeignKey("ImageId")]
    public ShimmieImage? Image { get; set; }

    [ForeignKey("UserId")]
    public ShimmieUser? User { get; set; }
}
