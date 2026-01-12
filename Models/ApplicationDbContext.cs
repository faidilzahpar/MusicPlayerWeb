using Microsoft.EntityFrameworkCore;

namespace MusicPlayerWeb.Models
{
    public class ApplicationDbContext : DbContext
    {
        // Constructor standar untuk EF Core (Sesuai Modul Bab 9)
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // Mendaftarkan semua Tabel (Entity) ke Database
        public DbSet<Song> Songs { get; set; }
        public DbSet<Artist> Artists { get; set; }
        public DbSet<Album> Albums { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<PlaylistSong> PlaylistSongs { get; set; }
    }
}