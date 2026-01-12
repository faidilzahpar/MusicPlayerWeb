using System.ComponentModel.DataAnnotations;

namespace MusicPlayerWeb.Models
{
    public class Artist
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; }

        public string? ImagePath { get; set; } // Boleh null

        // Relasi: Satu Artis punya banyak Album dan Lagu
        public virtual ICollection<Album>? Albums { get; set; }
        public virtual ICollection<Song>? Songs { get; set; }
    }
}