using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("users")]
public class ShimmieUser
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [MaxLength(32)]
    public string Name { get; set; } = string.Empty;

    [Column("pass")]
    [MaxLength(250)]
    public string? Pass { get; set; }

    [Column("joindate")]
    public DateTime JoinDate { get; set; }

    [Column("class")]
    [MaxLength(32)]
    public string Class { get; set; } = "user";

    [Column("email")]
    [MaxLength(128)]
    public string? Email { get; set; }
}
