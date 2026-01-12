using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MusicPlayerWeb.Models
{
    public class PlaylistSong
    {
        [Key]
        public int Id { get; set; }

        public int PlaylistId { get; set; }
        [ForeignKey("PlaylistId")]
        public virtual Playlist Playlist { get; set; }

        public int SongId { get; set; }
        [ForeignKey("SongId")]
        public virtual Song Song { get; set; }

        public int OrderIndex { get; set; } // Untuk urutan lagu
    }
}