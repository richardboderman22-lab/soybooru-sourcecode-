using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("images")]
public class ShimmieImage
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("owner_id")]
    public int OwnerId { get; set; }

    [Column("owner_ip")]
    [MaxLength(45)]
    public string OwnerIp { get; set; } = string.Empty;

    [Column("filename")]
    [MaxLength(64)]
    public string Filename { get; set; } = string.Empty;

    [Column("filesize")]
    public int Filesize { get; set; }

    [Column("hash")]
    [MaxLength(32)]
    public string Hash { get; set; } = string.Empty;

    [Column("ext")]
    [MaxLength(4)]
    public string Ext { get; set; } = string.Empty;

    [Column("source")]
    [MaxLength(255)]
    public string? Source { get; set; }

    [Column("width")]
    public int Width { get; set; }

    [Column("height")]
    public int Height { get; set; }

    [Column("posted")]
    public DateTime Posted { get; set; }

    [Column("locked")]
    public bool Locked { get; set; }

    [Column("lossless")]
    public bool? Lossless { get; set; }

    [Column("video")]
    public bool? Video { get; set; }

    [Column("audio")]
    public bool? Audio { get; set; }

    [Column("length")]
    public int? Length { get; set; }

    [Column("mime")]
    [MaxLength(512)]
    public string? Mime { get; set; }

    // Extension columns
    [Column("approved")]
    public bool Approved { get; set; }

    [Column("approved_by_id")]
    public int? ApprovedById { get; set; }

    [Column("author")]
    [MaxLength(255)]
    public string? Author { get; set; }

    [Column("favorites")]
    public int Favorites { get; set; }

    [Column("numeric_score")]
    public int NumericScore { get; set; }

    [Column("title")]
    [MaxLength(255)]
    public string? Title { get; set; }

    [Column("rating")]
    [MaxLength(1)]
    public string Rating { get; set; } = "u";

    [Column("trash")]
    public bool Trash { get; set; }

    // Navigation
    [ForeignKey("OwnerId")]
    public ShimmieUser? Owner { get; set; }
}
