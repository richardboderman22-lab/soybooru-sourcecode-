using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nuuru.Tools.ShimmieMigration.Source;

[Table("private_message")]
public class ShimmiePM
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("from_id")]
    public int FromId { get; set; }

    [Column("from_ip")]
    [MaxLength(45)]
    public string FromIp { get; set; } = string.Empty;

    [Column("to_id")]
    public int ToId { get; set; }

    [Column("sent_date")]
    public DateTime SentDate { get; set; }

    [Column("subject")]
    [MaxLength(64)]
    public string Subject { get; set; } = string.Empty;

    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Column("is_read")]
    public bool IsRead { get; set; }

    [ForeignKey("FromId")]
    public ShimmieUser? From { get; set; }

    [ForeignKey("ToId")]
    public ShimmieUser? To { get; set; }
}
