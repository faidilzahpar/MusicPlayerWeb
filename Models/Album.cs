using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MusicPlayerWeb.Models
{
    public class Album
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; }

        public int Year { get; set; }

        public string? CoverPath { get; set; } // Path gambar album

        // Foreign Key ke Artis
        public int? ArtistId { get; set; }

        [ForeignKey("ArtistId")]
        public virtual Artist? Artist { get; set; }

        // Relasi: Satu Album punya banyak Lagu
        public virtual ICollection<Song>? Songs { get; set; }
    }
}