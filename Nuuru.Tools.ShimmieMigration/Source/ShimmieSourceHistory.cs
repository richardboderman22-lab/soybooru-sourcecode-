using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("source_histories")]
public class ShimmieSourceHistory
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("image_id")]
    public int ImageId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("user_ip")]
    [MaxLength(45)]
    public string UserIp { get; set; } = string.Empty;

    [Column("source")]
    [MaxLength(2000)]
    public string Source { get; set; } = string.Empty;

    [Column("date_set")]
    public DateTime DateSet { get; set; }
}
