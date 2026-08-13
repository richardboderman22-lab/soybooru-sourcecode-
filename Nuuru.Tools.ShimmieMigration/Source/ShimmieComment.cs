using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("comments")]
public class ShimmieComment
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("image_id")]
    public int ImageId { get; set; }

    [Column("owner_id")]
    public int OwnerId { get; set; }

    [Column("owner_ip")]
    [MaxLength(45)]
    public string OwnerIp { get; set; } = string.Empty;

    [Column("posted")]
    public DateTime Posted { get; set; }

    [Column("comment")]
    public string Comment { get; set; } = string.Empty;

    [ForeignKey("ImageId")]
    public ShimmieImage? Image { get; set; }

    [ForeignKey("OwnerId")]
    public ShimmieUser? Owner { get; set; }
}
