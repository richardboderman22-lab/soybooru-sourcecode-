using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("tags")]
public class ShimmieTag
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("tag")]
    [MaxLength(64)]
    public string Tag { get; set; } = string.Empty;

    [Column("count")]
    public int Count { get; set; }
}
